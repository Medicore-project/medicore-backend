using System.Text.Json;
using Confluent.Kafka;
using MediCore.Contracts.Events.Appointment;
using MediCore.Patient.Application.Services;
using MediCore.Patient.Infrastructure.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace MediCore.Patient.Tests.Unit;

public sealed class AppointmentCompletedConsumerTests
{
    [Fact]
    public void Consumer_configuration_disables_automatic_offset_commits()
    {
        var config = AppointmentKafkaConsumerFactory.BuildConfig(Options());

        Assert.False(config.EnableAutoCommit ?? true);
        Assert.False(config.EnableAutoOffsetStore ?? true);
        Assert.Equal(AutoOffsetReset.Earliest, config.AutoOffsetReset);
        Assert.Equal("medicore-patient", config.GroupId);
    }

    [Fact]
    public async Task Successful_processing_commits_only_after_handler_completes()
    {
        var calls = new List<string>();
        var processor = new StubProcessor((_, _, _) =>
        {
            calls.Add("process");
            return Task.FromResult(AppointmentEventProcessingResult.Processed);
        });
        var client = new StubConsumerClient(Message("appointment.completed", calls), calls);
        using var provider = Services(processor);
        var consumer = CreateConsumer(provider);

        await consumer.ConsumeOnceAsync(client, CancellationToken.None);

        Assert.Equal(new[] { "consume", "process", "commit" }, calls);
        Assert.Equal(1, client.CommitCount);
        Assert.Equal(0, client.SeekCount);
    }

    [Theory]
    [InlineData(AppointmentEventProcessingResult.Duplicate)]
    [InlineData(AppointmentEventProcessingResult.DeadLetterQueued)]
    [InlineData(AppointmentEventProcessingResult.Ignored)]
    public async Task Durable_or_ignored_results_commit_the_offset(AppointmentEventProcessingResult result)
    {
        var processor = new StubProcessor((_, _, _) => Task.FromResult(result));
        var client = new StubConsumerClient(Message("appointment.completed"));
        using var provider = Services(processor);

        await CreateConsumer(provider).ConsumeOnceAsync(client, CancellationToken.None);

        Assert.Equal(1, client.CommitCount);
        Assert.Equal(0, client.SeekCount);
    }

    [Fact]
    public async Task Failed_processing_rewinds_message_without_committing()
    {
        var processor = new StubProcessor((_, _, _) =>
            throw new InvalidOperationException("database unavailable"));
        var message = Message("appointment.completed");
        var client = new StubConsumerClient(message);
        using var provider = Services(processor);

        await CreateConsumer(provider).ConsumeOnceAsync(client, CancellationToken.None);

        Assert.Equal(0, client.CommitCount);
        Assert.Equal(1, client.SeekCount);
        Assert.Equal(message.TopicPartitionOffset, client.LastSeek);
    }

    [Fact]
    public async Task Event_type_is_read_from_payload_when_header_is_absent()
    {
        string? capturedEventType = null;
        var processor = new StubProcessor((eventType, _, _) =>
        {
            capturedEventType = eventType;
            return Task.FromResult(AppointmentEventProcessingResult.Processed);
        });
        var client = new StubConsumerClient(MessageWithoutHeader("appointment.completed"));
        using var provider = Services(processor);

        await CreateConsumer(provider).ConsumeOnceAsync(client, CancellationToken.None);

        Assert.Equal("appointment.completed", capturedEventType);
        Assert.Equal(1, client.CommitCount);
    }

    [Fact]
    public async Task Processor_deserializes_completed_event_and_delegates_to_idempotent_handler()
    {
        var completedEvent = new AppointmentCompletedEvent
        {
            MessageId = Guid.NewGuid(),
            CorrelationId = "correlation-1",
            OccurredAtUtc = DateTime.UtcNow,
            AppointmentId = Guid.NewGuid(),
            PatientId = Guid.NewGuid(),
            Notes = "Visit completed"
        };
        var handler = new StubHandler(AppointmentCompletedHandlingResult.Duplicate);
        var processor = new AppointmentEventProcessor(handler);

        var result = await processor.ProcessAsync(
            "appointment.completed",
            JsonSerializer.Serialize(completedEvent, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
            CancellationToken.None);

        Assert.Equal(AppointmentEventProcessingResult.Duplicate, result);
        Assert.NotNull(handler.Received);
        Assert.Equal(completedEvent.MessageId, handler.Received!.MessageId);
        Assert.Equal(completedEvent.AppointmentId, handler.Received.AppointmentId);
    }

    [Fact]
    public async Task Processor_ignores_other_appointment_events_without_deserializing_them()
    {
        var handler = new StubHandler(AppointmentCompletedHandlingResult.Processed);
        var processor = new AppointmentEventProcessor(handler);

        var result = await processor.ProcessAsync(
            "appointment.booked",
            "not a completed-event payload",
            CancellationToken.None);

        Assert.Equal(AppointmentEventProcessingResult.Ignored, result);
        Assert.Null(handler.Received);
    }

    private static AppointmentCompletedConsumer CreateConsumer(ServiceProvider provider) => new(
        new UnusedConsumerFactory(),
        provider.GetRequiredService<IServiceScopeFactory>(),
        Options(),
        NullLogger<AppointmentCompletedConsumer>.Instance);

    private static ServiceProvider Services(IAppointmentEventProcessor processor) =>
        new ServiceCollection()
            .AddScoped<IAppointmentEventProcessor>(_ => processor)
            .BuildServiceProvider();

    private static AppointmentConsumerOptions Options() => new()
    {
        BootstrapServers = "localhost:9092",
        RetryDelay = TimeSpan.Zero
    };

    private static ConsumeResult<string, string> Message(string eventType, List<string>? calls = null) => new()
    {
        Topic = "appointment-events",
        Partition = new Partition(0),
        Offset = new Offset(7),
        Message = new Message<string, string>
        {
            Key = Guid.NewGuid().ToString(),
            Value = $"{{\"eventType\":\"{eventType}\"}}",
            Headers = new Headers { new Header("event-type", System.Text.Encoding.UTF8.GetBytes(eventType)) }
        }
    };

    private static ConsumeResult<string, string> MessageWithoutHeader(string eventType) => new()
    {
        Topic = "appointment-events",
        Partition = new Partition(0),
        Offset = new Offset(8),
        Message = new Message<string, string>
        {
            Key = Guid.NewGuid().ToString(),
            Value = $"{{\"eventType\":\"{eventType}\"}}"
        }
    };

    private sealed class StubProcessor : IAppointmentEventProcessor
    {
        private readonly Func<string?, string, CancellationToken, Task<AppointmentEventProcessingResult>> _process;

        public StubProcessor(Func<string?, string, CancellationToken, Task<AppointmentEventProcessingResult>> process)
        {
            _process = process;
        }

        public Task<AppointmentEventProcessingResult> ProcessAsync(
            string? eventType,
            string payload,
            CancellationToken cancellationToken) => _process(eventType, payload, cancellationToken);
    }

    private sealed class StubHandler : IAppointmentCompletedHandler
    {
        private readonly AppointmentCompletedHandlingResult _result;

        public StubHandler(AppointmentCompletedHandlingResult result)
        {
            _result = result;
        }

        public AppointmentCompletedEvent? Received { get; private set; }

        public Task<AppointmentCompletedHandlingResult> HandleAsync(
            AppointmentCompletedEvent completedEvent,
            CancellationToken cancellationToken = default)
        {
            Received = completedEvent;
            return Task.FromResult(_result);
        }
    }

    private sealed class StubConsumerClient : IAppointmentKafkaConsumerClient
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

    private sealed class UnusedConsumerFactory : IAppointmentKafkaConsumerFactory
    {
        public IAppointmentKafkaConsumerClient Create() =>
            throw new InvalidOperationException("The factory is not used by ConsumeOnceAsync tests.");
    }
}
