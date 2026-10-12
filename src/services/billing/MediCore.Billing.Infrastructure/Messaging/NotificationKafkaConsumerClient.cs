using Confluent.Kafka;

namespace MediCore.Billing.Infrastructure.Messaging;

public interface INotificationKafkaConsumerClient : IDisposable
{
    void Subscribe(IEnumerable<string> topics);
    ConsumeResult<string, string> Consume(CancellationToken cancellationToken);
    void Commit(ConsumeResult<string, string> result);
    void Seek(TopicPartitionOffset offset);
    void Close();
}

public interface INotificationKafkaConsumerFactory
{
    INotificationKafkaConsumerClient Create();
}

public sealed class NotificationKafkaConsumerFactory : INotificationKafkaConsumerFactory
{
    private readonly NotificationConsumerOptions _options;

    public NotificationKafkaConsumerFactory(NotificationConsumerOptions options)
    {
        _options = options;
    }

    public INotificationKafkaConsumerClient Create() =>
        new NotificationKafkaConsumerClient(
            new ConsumerBuilder<string, string>(BuildConfig(_options)).Build());

    public static ConsumerConfig BuildConfig(NotificationConsumerOptions options)
    {
        var config = new ConsumerConfig
        {
            BootstrapServers = options.BootstrapServers,
            GroupId = options.GroupId,
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false,
            EnableAutoOffsetStore = false
        };

        if (!string.IsNullOrWhiteSpace(options.SaslUsername) && !string.IsNullOrWhiteSpace(options.SaslPassword))
        {
            config.SecurityProtocol = SecurityProtocol.SaslSsl;
            config.SaslMechanism = SaslMechanism.Plain;
            config.SaslUsername = options.SaslUsername;
            config.SaslPassword = options.SaslPassword;
        }

        return config;
    }

    private sealed class NotificationKafkaConsumerClient : INotificationKafkaConsumerClient
    {
        private readonly IConsumer<string, string> _consumer;

        public NotificationKafkaConsumerClient(IConsumer<string, string> consumer)
        {
            _consumer = consumer;
        }

        public void Subscribe(IEnumerable<string> topics) => _consumer.Subscribe(topics);
        public ConsumeResult<string, string> Consume(CancellationToken cancellationToken) => _consumer.Consume(cancellationToken);
        public void Commit(ConsumeResult<string, string> result) => _consumer.Commit(result);
        public void Seek(TopicPartitionOffset offset) => _consumer.Seek(offset);
        public void Close() => _consumer.Close();
        public void Dispose() => _consumer.Dispose();
    }
}
