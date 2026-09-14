using Confluent.Kafka;

namespace MediCore.Patient.Infrastructure.Messaging;

public interface IAppointmentKafkaConsumerClient : IDisposable
{
    void Subscribe(string topic);
    ConsumeResult<string, string> Consume(CancellationToken cancellationToken);
    void Commit(ConsumeResult<string, string> result);
    void Seek(TopicPartitionOffset offset);
    void Close();
}

public interface IAppointmentKafkaConsumerFactory
{
    IAppointmentKafkaConsumerClient Create();
}

public sealed class AppointmentKafkaConsumerFactory : IAppointmentKafkaConsumerFactory
{
    private readonly AppointmentConsumerOptions _options;

    public AppointmentKafkaConsumerFactory(AppointmentConsumerOptions options)
    {
        _options = options;
    }

    public IAppointmentKafkaConsumerClient Create() =>
        new AppointmentKafkaConsumerClient(
            new ConsumerBuilder<string, string>(BuildConfig(_options)).Build());

    public static ConsumerConfig BuildConfig(AppointmentConsumerOptions options) => new()
    {
        BootstrapServers = options.BootstrapServers,
        GroupId = options.GroupId,
        AutoOffsetReset = AutoOffsetReset.Earliest,
        EnableAutoCommit = false,
        EnableAutoOffsetStore = false
    };

    private sealed class AppointmentKafkaConsumerClient : IAppointmentKafkaConsumerClient
    {
        private readonly IConsumer<string, string> _consumer;

        public AppointmentKafkaConsumerClient(IConsumer<string, string> consumer)
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
