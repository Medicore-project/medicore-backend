using System.Text;
using System.Text.Json;
using Confluent.Kafka;
using MediCore.Appointment.Application.Services;
using MediCore.Appointment.Infrastructure;
using MediCore.Appointment.Infrastructure.Messaging;
using MediCore.Contracts.Events.Staff;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace MediCore.Appointment.Tests.Unit;

public sealed class StaffEventsConsumerTests
{
    // ── Consumer ──────────────────────────────────────────────────────────────

    [Fact]
    public void Consumer_configuration_reads_from_the_start_and_disables_automatic_commits()
    {
        var config = StaffKafkaConsumerFactory.BuildConfig(Options());

        Assert.False(config.EnableAutoCommit ?? true);
        Assert.False(config.EnableAutoOffsetStore ?? true);
        Assert.Equal(AutoOffsetReset.Earliest, config.AutoOffsetReset);
        Assert.Equal("medicore-appointment", config.GroupId);
        Assert.Equal("staff-events", Options().Topic);
    }

    [Fact]
    public async Task Successful_processing_commits_only_after_the_processor_completes()
    {
        var calls = new List<string>();
        var processor = new StubProcessor((_, _) =>
        {
            calls.Add("process");
            return Task.FromResult<StaffEventResult>(new StaffEventAppliedResult());
        });
        var client = new StubConsumerClient(Message("staff.updated"), calls);
        using var provider = Services(processor);

        await CreateConsumer(provider).ConsumeOnceAsync(client, CancellationToken.None);

        string[] expectedOrder = ["consume", "process", "commit"];
        Assert.Equal(expectedOrder, calls);
        Assert.Equal(0, client.SeekCount);
    }

    public static TheoryData<StaffEventResult> FinalResults => new()
    {
        new StaffEventDuplicateResult(),
        new StaffEventStaleResult(),
        new StaffEventIgnoredResult("NotADoctor"),
        new StaffEventRejectedResult("MalformedPayload")
    };

    [Theory]
    [MemberData(nameof(FinalResults))]
    public async Task Skipped_messages_are_still_committed_so_they_cannot_block_the_partition(StaffEventResult result)
    {
        var processor = new StubProcessor((_, _) => Task.FromResult(result));
        var client = new StubConsumerClient(Message("staff.updated"));
        using var provider = Services(processor);

        await CreateConsumer(provider).ConsumeOnceAsync(client, CancellationToken.None);

        Assert.Equal(1, client.CommitCount);
        Assert.Equal(0, client.SeekCount);
    }

    [Fact]
    public async Task A_failure_rewinds_to_the_message_without_committing_it()
    {
        var processor = new StubProcessor((_, _) =>
            throw new InvalidOperationException("database unavailable"));
        var message = Message("staff.updated");
        var client = new StubConsumerClient(message);
        using var provider = Services(processor);

        await CreateConsumer(provider).ConsumeOnceAsync(client, CancellationToken.None);

        Assert.Equal(0, client.CommitCount);
        Assert.Equal(1, client.SeekCount);
        Assert.Equal(message.TopicPartitionOffset, client.LastSeek);
    }

    [Fact]
    public async Task The_event_type_is_read_from_the_payload_when_the_header_is_absent()
    {
        string? capturedEventType = null;
        var processor = new StubProcessor((eventType, _) =>
        {
            capturedEventType = eventType;
            return Task.FromResult<StaffEventResult>(new StaffEventAppliedResult());
        });
        var client = new StubConsumerClient(Message("staff.deactivated", withHeader: false));
        using var provider = Services(processor);

        await CreateConsumer(provider).ConsumeOnceAsync(client, CancellationToken.None);

        Assert.Equal("staff.deactivated", capturedEventType);
    }

    [Fact]
    public async Task An_unreadable_payload_with_no_header_reaches_the_processor_instead_of_retrying_forever()
    {
        var processed = false;
        var processor = new StubProcessor((eventType, _) =>
        {
            processed = true;
            Assert.Null(eventType);
            return Task.FromResult<StaffEventResult>(new StaffEventRejectedResult("MissingEventType"));
        });
        var client = new StubConsumerClient(Message(eventType: null, payload: "{not json"));
        using var provider = Services(processor);

        await CreateConsumer(provider).ConsumeOnceAsync(client, CancellationToken.None);

        Assert.True(processed);
        Assert.Equal(1, client.CommitCount);
        Assert.Equal(0, client.SeekCount);
    }

    // ── Processor ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task The_processor_reads_an_update_serialized_the_way_identity_writes_it()
    {
        var updated = new StaffUpdatedEvent
        {
            StaffId = Guid.NewGuid(),
            FullName = "Nimal Perera",
            Specialization = "Cardiology",
            DepartmentId = Guid.NewGuid(),
            Role = "Doctor",
            IsActive = false,
            OccurredAtUtc = new DateTime(2026, 9, 22, 6, 0, 0, DateTimeKind.Utc)
        };
        var handler = new StubHandler();

        // Identity serializes with default options, so property names arrive PascalCase.
        var result = await Processor(handler).ProcessAsync(
            "staff.updated",
            JsonSerializer.Serialize(updated),
            CancellationToken.None);

        Assert.IsType<StaffEventAppliedResult>(result);
        var received = Assert.IsType<StaffUpdatedEvent>(handler.Received);
        Assert.Equal(updated.MessageId, received.MessageId);
        Assert.Equal(updated.StaffId, received.StaffId);
        Assert.Equal("Doctor", received.Role);
        Assert.False(received.IsActive);
        Assert.Equal(updated.OccurredAtUtc, received.OccurredAtUtc);
        Assert.Equal(DateTimeKind.Utc, received.OccurredAtUtc.Kind);
    }

    [Fact]
    public async Task The_processor_reads_an_update_published_before_role_and_active_existed()
    {
        var handler = new StubHandler();
        var legacyPayload = $$"""
            {"StaffId":"{{Guid.NewGuid()}}","FullName":"Nimal Perera","Specialization":"Cardiology",
             "DepartmentId":"{{Guid.NewGuid()}}","MessageId":"{{Guid.NewGuid()}}","CorrelationId":"",
             "OccurredAtUtc":"2026-09-01T06:00:00Z","EventType":"staff.updated","Version":1}
            """;

        await Processor(handler).ProcessAsync("staff.updated", legacyPayload, CancellationToken.None);

        var received = Assert.IsType<StaffUpdatedEvent>(handler.Received);
        Assert.Null(received.Role);
        Assert.Null(received.IsActive);
    }

    [Fact]
    public async Task The_processor_routes_created_and_deactivated_events_to_their_handlers()
    {
        var handler = new StubHandler();
        var processor = Processor(handler);

        await processor.ProcessAsync(
            "staff.created",
            JsonSerializer.Serialize(new StaffCreatedEvent
            {
                StaffId = Guid.NewGuid(),
                UserId = Guid.NewGuid(),
                FullName = "Nimal Perera",
                Email = "nimal@medicore.test",
                Role = "Doctor",
                Specialization = "Cardiology",
                DepartmentId = Guid.NewGuid()
            }),
            CancellationToken.None);
        Assert.IsType<StaffCreatedEvent>(handler.Received);

        await processor.ProcessAsync(
            "STAFF.DEACTIVATED",
            JsonSerializer.Serialize(new StaffDeactivatedEvent { StaffId = Guid.NewGuid() }),
            CancellationToken.None);
        Assert.IsType<StaffDeactivatedEvent>(handler.Received);
    }

    [Theory]
    [InlineData("{not json")]
    [InlineData("")]
    [InlineData("{\"StaffId\":\"3f5b9c1e-1f0a-4a7c-9b3d-7c9a2e4f6b18\"}")]
    public async Task A_payload_that_cannot_be_read_is_rejected_not_thrown(string payload)
    {
        var handler = new StubHandler();

        var result = await Processor(handler).ProcessAsync("staff.updated", payload, CancellationToken.None);

        Assert.Equal(new StaffEventRejectedResult("MalformedPayload"), result);
        Assert.Null(handler.Received);
    }

    [Fact]
    public async Task Other_staff_event_types_are_ignored_without_being_read()
    {
        var handler = new StubHandler();

        var result = await Processor(handler).ProcessAsync(
            "staff.password-reset",
            "not a staff payload",
            CancellationToken.None);

        Assert.Equal(new StaffEventIgnoredResult("UnhandledEventType"), result);
        Assert.Null(handler.Received);
    }

    [Fact]
    public async Task A_message_with_no_event_type_is_rejected()
    {
        var result = await Processor(new StubHandler()).ProcessAsync(null, "{}", CancellationToken.None);

        Assert.Equal(new StaffEventRejectedResult("MissingEventType"), result);
    }

    // ── Registration ──────────────────────────────────────────────────────────

    [Fact]
    public void The_consumer_is_registered_with_configured_topic_and_group_when_kafka_is_set()
    {
        var services = Infrastructure(new Dictionary<string, string?>
        {
            ["Kafka:BootstrapServers"] = "kafka:9092",
            ["Kafka:StaffConsumer:GroupId"] = "custom-group",
            ["Kafka:StaffConsumer:RetryDelaySeconds"] = "5"
        });

        Assert.Contains(services, d => d.ImplementationType == typeof(StaffEventsConsumer));
        var options = Assert.IsType<StaffConsumerOptions>(
            services.Single(d => d.ServiceType == typeof(StaffConsumerOptions)).ImplementationInstance);
        Assert.Equal("kafka:9092", options.BootstrapServers);
        Assert.Equal("staff-events", options.Topic);
        Assert.Equal("custom-group", options.GroupId);
        Assert.Equal(TimeSpan.FromSeconds(5), options.RetryDelay);
    }

    [Fact]
    public void Without_kafka_configured_the_consumer_is_not_registered_so_the_host_still_starts()
    {
        var services = Infrastructure([]);

        Assert.DoesNotContain(services, d => d.ImplementationType == typeof(StaffEventsConsumer));
        Assert.DoesNotContain(services, d => d.ServiceType == typeof(StaffConsumerOptions));
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static IServiceCollection Infrastructure(Dictionary<string, string?> settings)
    {
        settings["ConnectionStrings:AppointmentDatabase"] = "Host=localhost;Database=unused";
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        return new ServiceCollection().AddInfrastructure(configuration);
    }

    private static StaffEventProcessor Processor(StubHandler handler) =>
        new(handler, NullLogger<StaffEventProcessor>.Instance);

    private static StaffEventsConsumer CreateConsumer(ServiceProvider provider) => new(
        new UnusedConsumerFactory(),
        provider.GetRequiredService<IServiceScopeFactory>(),
        Options(),
        NullLogger<StaffEventsConsumer>.Instance);

    private static ServiceProvider Services(IStaffEventProcessor processor) =>
        new ServiceCollection()
            .AddScoped(_ => processor)
            .BuildServiceProvider();

    private static StaffConsumerOptions Options() => new()
    {
        BootstrapServers = "localhost:9092",
        RetryDelay = TimeSpan.Zero
    };

    private static ConsumeResult<string, string> Message(
        string? eventType,
        bool withHeader = true,
        string? payload = null) => new()
        {
            Topic = "staff-events",
            Partition = new Partition(1),
            Offset = new Offset(42),
            Message = new Message<string, string>
            {
                Key = Guid.NewGuid().ToString(),
                Value = payload ?? $"{{\"EventType\":\"{eventType}\"}}",
                Headers = withHeader && eventType is not null
                    ? [new Header("event-type", Encoding.UTF8.GetBytes(eventType))]
                    : []
            }
        };

    private sealed class StubProcessor : IStaffEventProcessor
    {
        private readonly Func<string?, string?, Task<StaffEventResult>> _process;

        public StubProcessor(Func<string?, string?, Task<StaffEventResult>> process)
        {
            _process = process;
        }

        public Task<StaffEventResult> ProcessAsync(
            string? eventType,
            string? payload,
            CancellationToken cancellationToken) => _process(eventType, payload);
    }

    private sealed class StubHandler : IStaffEventHandler
    {
        public object? Received { get; private set; }

        public Task<StaffEventResult> HandleCreatedAsync(
            StaffCreatedEvent createdEvent,
            CancellationToken cancellationToken = default) => Receive(createdEvent);

        public Task<StaffEventResult> HandleUpdatedAsync(
            StaffUpdatedEvent updatedEvent,
            CancellationToken cancellationToken = default) => Receive(updatedEvent);

        public Task<StaffEventResult> HandleDeactivatedAsync(
            StaffDeactivatedEvent deactivatedEvent,
            CancellationToken cancellationToken = default) => Receive(deactivatedEvent);

        private Task<StaffEventResult> Receive(object staffEvent)
        {
            Received = staffEvent;
            return Task.FromResult<StaffEventResult>(new StaffEventAppliedResult());
        }
    }

    private sealed class StubConsumerClient : IStaffKafkaConsumerClient
    {
        private readonly ConsumeResult<string, string> _message;
        private readonly List<string>? _calls;

        public StubConsumerClient(ConsumeResult<string, string> message, List<string>? calls = null)
        {
            _message = message;
            _calls = calls;
        }

        public int CommitCount { get; private set; }
        public int SeekCount { get; private set; }
        public TopicPartitionOffset? LastSeek { get; private set; }

        public void Subscribe(string topic) { }

        public ConsumeResult<string, string> Consume(CancellationToken cancellationToken)
        {
            _calls?.Add("consume");
            return _message;
        }

        public void Commit(ConsumeResult<string, string> result)
        {
            _calls?.Add("commit");
            CommitCount++;
        }

        public void Seek(TopicPartitionOffset offset)
        {
            SeekCount++;
            LastSeek = offset;
        }

        public void Close() { }
        public void Dispose() { }
    }

    private sealed class UnusedConsumerFactory : IStaffKafkaConsumerFactory
    {
        public IStaffKafkaConsumerClient Create() =>
            throw new InvalidOperationException("The factory is not used by ConsumeOnceAsync tests.");
    }
}
