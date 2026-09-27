using MediCore.Appointment.Application.Entities;
using MediCore.Appointment.Application.Interfaces;
using MediCore.Appointment.Application.Messaging;
using MediCore.Appointment.Infrastructure.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace MediCore.Appointment.Tests.Unit;

/// <summary>
/// SCRUM-34 AC5 — when Kafka is unavailable the appointment still saves and the event is retried
/// from the outbox. The saving half is structural (the booking service has no publisher at all);
/// this covers the retrying half.
/// </summary>
public sealed class OutboxProcessorTests
{
    [Fact]
    public async Task A_published_message_is_stamped_and_never_looked_at_again()
    {
        var fixture = new Fixture(Row());

        await fixture.Processor.ProcessBatchAsync(CancellationToken.None);

        var message = Assert.Single(fixture.Repository.Rows);
        Assert.NotNull(message.ProcessedOnUtc);
        Assert.Null(message.Error);
        Assert.Equal(0, message.RetryCount);
        Assert.Equal(1, fixture.Repository.SaveCount);
        Assert.Empty(await fixture.Repository.GetUnprocessedBatchAsync(20));
    }

    [Fact]
    public async Task A_broker_that_refuses_the_message_leaves_it_unprocessed_and_counts_the_attempt()
    {
        var fixture = new Fixture(Row());
        fixture.Publisher.Fail = new InvalidOperationException("Local: Message timed out");

        await fixture.Processor.ProcessBatchAsync(CancellationToken.None);

        var message = Assert.Single(fixture.Repository.Rows);
        Assert.Null(message.ProcessedOnUtc);
        Assert.Equal(1, message.RetryCount);
        Assert.Equal("Local: Message timed out", message.Error);
        // Still saved: the retry count is the record that an attempt happened.
        Assert.Equal(1, fixture.Repository.SaveCount);
    }

    [Fact]
    public async Task The_failed_message_comes_back_on_the_next_pass_and_succeeds_once_the_broker_returns()
    {
        var fixture = new Fixture(Row());
        fixture.Publisher.Fail = new InvalidOperationException("broker down");

        await fixture.Processor.ProcessBatchAsync(CancellationToken.None);

        // The broker comes back. Nothing re-queues the message; the next batch simply finds it,
        // because an unprocessed row is the queue.
        fixture.Publisher.Fail = null;
        Assert.Single(await fixture.Repository.GetUnprocessedBatchAsync(20));

        await fixture.Processor.ProcessBatchAsync(CancellationToken.None);

        var message = Assert.Single(fixture.Repository.Rows);
        Assert.NotNull(message.ProcessedOnUtc);
        Assert.Null(message.Error);
        // The count stays, so a reader can still see it took two attempts.
        Assert.Equal(1, message.RetryCount);
        Assert.Equal(2, fixture.Repository.SaveCount);
    }

    [Fact]
    public async Task One_failure_does_not_hold_back_the_rest_of_the_batch()
    {
        var first = Row();
        var second = Row();
        var fixture = new Fixture(first, second);
        fixture.Publisher.FailFor = first.MessageId;

        await fixture.Processor.ProcessBatchAsync(CancellationToken.None);

        Assert.Null(first.ProcessedOnUtc);
        Assert.Equal(1, first.RetryCount);
        Assert.NotNull(second.ProcessedOnUtc);
        Assert.Equal(0, second.RetryCount);
        // One save for the whole batch, not one per message.
        Assert.Equal(1, fixture.Repository.SaveCount);
    }

    // ── SCRUM-36: one appointment's events stay in order ─────────────────────

    [Fact]
    public async Task A_cancellation_never_overtakes_a_booking_that_failed_to_publish()
    {
        // The ticket's ordering note. Both share the appointment's key, so the broker keeps them
        // in order only if they reach it in order.
        var appointmentKey = Guid.NewGuid().ToString();
        var booked = Row(new DateTime(2026, 9, 23, 9, 0, 0, DateTimeKind.Utc), appointmentKey, "appointment.booked");
        var cancelled = Row(new DateTime(2026, 9, 23, 9, 5, 0, DateTimeKind.Utc), appointmentKey, "appointment.cancelled");
        var fixture = new Fixture(booked, cancelled);
        fixture.Publisher.FailFor = booked.MessageId;

        await fixture.Processor.ProcessBatchAsync(CancellationToken.None);

        Assert.Empty(fixture.Publisher.Published);
        Assert.Null(booked.ProcessedOnUtc);
        Assert.Equal(1, booked.RetryCount);
        // Held back, not failed: it was never attempted, so it carries no retry and no error.
        Assert.Null(cancelled.ProcessedOnUtc);
        Assert.Equal(0, cancelled.RetryCount);
        Assert.Null(cancelled.Error);
    }

    [Fact]
    public async Task The_held_back_event_follows_its_booking_on_the_next_pass()
    {
        var appointmentKey = Guid.NewGuid().ToString();
        var booked = Row(new DateTime(2026, 9, 23, 9, 0, 0, DateTimeKind.Utc), appointmentKey, "appointment.booked");
        var cancelled = Row(new DateTime(2026, 9, 23, 9, 5, 0, DateTimeKind.Utc), appointmentKey, "appointment.cancelled");
        var fixture = new Fixture(booked, cancelled);
        fixture.Publisher.FailFor = booked.MessageId;

        await fixture.Processor.ProcessBatchAsync(CancellationToken.None);
        fixture.Publisher.FailFor = null;
        await fixture.Processor.ProcessBatchAsync(CancellationToken.None);

        Assert.Equal([booked.MessageId, cancelled.MessageId], fixture.Publisher.Published);
        Assert.NotNull(booked.ProcessedOnUtc);
        Assert.NotNull(cancelled.ProcessedOnUtc);
    }

    [Fact]
    public async Task Every_later_event_of_that_appointment_waits_but_other_appointments_do_not()
    {
        // Booked, rescheduled-then-cancelled, completed... all held behind the one that failed.
        var appointmentKey = Guid.NewGuid().ToString();
        var booked = Row(new DateTime(2026, 9, 23, 9, 0, 0, DateTimeKind.Utc), appointmentKey, "appointment.booked");
        var otherAppointment = Row(new DateTime(2026, 9, 23, 9, 1, 0, DateTimeKind.Utc));
        var cancelled = Row(new DateTime(2026, 9, 23, 9, 2, 0, DateTimeKind.Utc), appointmentKey, "appointment.cancelled");
        var completed = Row(new DateTime(2026, 9, 23, 9, 3, 0, DateTimeKind.Utc), appointmentKey, "appointment.completed");
        var fixture = new Fixture(booked, otherAppointment, cancelled, completed);
        fixture.Publisher.FailFor = booked.MessageId;

        await fixture.Processor.ProcessBatchAsync(CancellationToken.None);

        Assert.Equal([otherAppointment.MessageId], fixture.Publisher.Published);
        Assert.Null(cancelled.ProcessedOnUtc);
        Assert.Null(completed.ProcessedOnUtc);
    }

    [Fact]
    public async Task A_later_failure_does_not_hold_back_what_came_before_it()
    {
        // Only what follows a failure waits; the booking ahead of a failed cancellation is sent.
        var appointmentKey = Guid.NewGuid().ToString();
        var booked = Row(new DateTime(2026, 9, 23, 9, 0, 0, DateTimeKind.Utc), appointmentKey, "appointment.booked");
        var cancelled = Row(new DateTime(2026, 9, 23, 9, 5, 0, DateTimeKind.Utc), appointmentKey, "appointment.cancelled");
        var fixture = new Fixture(booked, cancelled);
        fixture.Publisher.FailFor = cancelled.MessageId;

        await fixture.Processor.ProcessBatchAsync(CancellationToken.None);

        Assert.Equal([booked.MessageId], fixture.Publisher.Published);
        Assert.Equal(1, cancelled.RetryCount);
    }

    [Fact]
    public async Task An_empty_outbox_writes_nothing()
    {
        var fixture = new Fixture();

        await fixture.Processor.ProcessBatchAsync(CancellationToken.None);

        Assert.Equal(0, fixture.Repository.SaveCount);
        Assert.Equal(0, fixture.Publisher.PublishCount);
    }

    [Fact]
    public async Task An_unreasonably_long_broker_error_is_truncated_to_the_column_width()
    {
        var fixture = new Fixture(Row());
        fixture.Publisher.Fail = new InvalidOperationException(new string('x', 5_000));

        await fixture.Processor.ProcessBatchAsync(CancellationToken.None);

        Assert.Equal(2_000, Assert.Single(fixture.Repository.Rows).Error!.Length);
    }

    [Fact]
    public async Task Messages_are_drained_oldest_first()
    {
        var newer = Row(new DateTime(2026, 9, 23, 10, 0, 0, DateTimeKind.Utc));
        var older = Row(new DateTime(2026, 9, 23, 9, 0, 0, DateTimeKind.Utc));
        var fixture = new Fixture(newer, older);

        await fixture.Processor.ProcessBatchAsync(CancellationToken.None);

        Assert.Equal([older.MessageId, newer.MessageId], fixture.Publisher.Published);
    }

    private static OutboxMessage Row(
        DateTime? occurredOnUtc = null,
        string? eventKey = null,
        string eventType = "appointment.booked") => new()
    {
        MessageId = Guid.NewGuid(),
        Topic = AppointmentOutboxMessages.Topic,
        EventKey = eventKey ?? Guid.NewGuid().ToString(),
        EventType = eventType,
        CorrelationId = Guid.NewGuid().ToString(),
        Payload = "{}",
        OccurredOnUtc = occurredOnUtc ?? new DateTime(2026, 9, 23, 9, 0, 0, DateTimeKind.Utc)
    };

    private sealed class Fixture
    {
        public Fixture(params OutboxMessage[] rows)
        {
            Repository = new FakeOutboxMessageRepository(rows);
            Publisher = new FakePublisher();

            // A real scope factory, because the processor opens a scope per batch.
            var services = new ServiceCollection();
            services.AddScoped<IOutboxMessageRepository>(_ => Repository);
            services.AddScoped<IKafkaEventPublisher>(_ => Publisher);
            var provider = services.BuildServiceProvider();

            Processor = new OutboxProcessor(
                provider.GetRequiredService<IServiceScopeFactory>(),
                NullLogger<OutboxProcessor>.Instance);
        }

        public FakeOutboxMessageRepository Repository { get; }

        public FakePublisher Publisher { get; }

        public OutboxProcessor Processor { get; }
    }

    private sealed class FakeOutboxMessageRepository : IOutboxMessageRepository
    {
        public FakeOutboxMessageRepository(IEnumerable<OutboxMessage> rows)
        {
            Rows = [.. rows];
        }

        public List<OutboxMessage> Rows { get; }

        public int SaveCount { get; private set; }

        public Task<IReadOnlyList<OutboxMessage>> GetUnprocessedBatchAsync(
            int batchSize,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<OutboxMessage>>(
            [
                .. Rows
                    .Where(row => row.ProcessedOnUtc is null)
                    .OrderBy(row => row.OccurredOnUtc)
                    .Take(batchSize)
            ]);

        public Task SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            SaveCount++;
            return Task.CompletedTask;
        }

        public Task AddAsync(OutboxMessage message, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("The dispatcher only drains; booking writes the rows.");
    }

    private sealed class FakePublisher : IKafkaEventPublisher
    {
        public Exception? Fail { get; set; }

        public Guid? FailFor { get; set; }

        public List<Guid> Published { get; } = [];

        public int PublishCount => Published.Count;

        public Task PublishAsync(OutboxMessage message, CancellationToken cancellationToken = default)
        {
            if (Fail is not null || FailFor == message.MessageId)
            {
                throw Fail ?? new InvalidOperationException("broker refused the message");
            }

            Published.Add(message.MessageId);
            return Task.CompletedTask;
        }
    }
}
