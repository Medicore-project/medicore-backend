using Confluent.Kafka;

namespace MediCore.Appointment.Infrastructure.Messaging;

/// <summary>The slice of <see cref="IConsumer{TKey,TValue}"/> the consumer uses, so tests can stub it.</summary>
public interface IStaffKafkaConsumerClient : IDisposable
{
    void Subscribe(string topic);
    ConsumeResult<string, string> Consume(CancellationToken cancellationToken);
    void Commit(ConsumeResult<string, string> result);
    void Seek(TopicPartitionOffset offset);
    void Close();
}

public interface IStaffKafkaConsumerFactory
{
    IStaffKafkaConsumerClient Create();
}

public sealed class StaffKafkaConsumerFactory : IStaffKafkaConsumerFactory
{
    private readonly StaffConsumerOptions _options;

    public StaffKafkaConsumerFactory(StaffConsumerOptions options)
    {
        _options = options;
    }

    public IStaffKafkaConsumerClient Create() =>
        new StaffKafkaConsumerClient(
            new ConsumerBuilder<string, string>(BuildConfig(_options)).Build());

    public static ConsumerConfig BuildConfig(StaffConsumerOptions options) => new()
    {
        BootstrapServers = options.BootstrapServers,
        GroupId = options.GroupId,

        // A new group starts from the oldest retained event, not only ones published after it joined.
        AutoOffsetReset = AutoOffsetReset.Earliest,

        // Offsets are committed by hand, only once the cache and the processed-message row are saved.
        EnableAutoCommit = false,
        EnableAutoOffsetStore = false
    };

    private sealed class StaffKafkaConsumerClient : IStaffKafkaConsumerClient
    {
        private readonly IConsumer<string, string> _consumer;

        public StaffKafkaConsumerClient(IConsumer<string, string> consumer)
        {
            _consumer = consumer;
        }

        public void Subscribe(string topic) => _consumer.Subscribe(topic);

        public ConsumeResult<string, string> Consume(CancellationToken cancellationToken) =>
            _consumer.Consume(cancellationToken);

        public void Commit(ConsumeResult<string, string> result) => _consumer.Commit(result);

        public void Seek(TopicPartitionOffset offset) => _consumer.Seek(offset);

        public void Close() => _consumer.Close();

        public void Dispose() => _consumer.Dispose();
    }
}
