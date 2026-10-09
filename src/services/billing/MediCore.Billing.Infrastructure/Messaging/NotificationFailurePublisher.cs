using System.Globalization;
using System.Text;
using Confluent.Kafka;

namespace MediCore.Billing.Infrastructure.Messaging;

public interface INotificationFailurePublisher
{
    Task<NotificationFailureRoute> PublishAsync(
        ConsumeResult<string, string> consumed,
        Exception exception,
        CancellationToken cancellationToken);
}

public sealed record NotificationFailureRoute(string Topic, int Attempt, bool IsDeadLetter);

public sealed class NotificationFailurePublisher : INotificationFailurePublisher
{
    private const string RetryAttemptHeader = "retry-attempt";
    private const string OriginalTopicHeader = "original-topic";
    private const string FailureReasonHeader = "failure-reason";
    private readonly IProducer<string, string> _producer;
    private readonly NotificationConsumerOptions _options;

    public NotificationFailurePublisher(
        IProducer<string, string> producer,
        NotificationConsumerOptions options)
    {
        _producer = producer;
        _options = options;
    }

    public async Task<NotificationFailureRoute> PublishAsync(
        ConsumeResult<string, string> consumed,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var route = DetermineRoute(consumed);
        var originalTopic = route.Topic[..^(route.IsDeadLetter ? ".dlt".Length : ".retry".Length)];
        var headers = CopyHeaders(consumed.Message.Headers);
        headers.Add(RetryAttemptHeader, Encoding.UTF8.GetBytes(route.Attempt.ToString(CultureInfo.InvariantCulture)));
        headers.Add(OriginalTopicHeader, Encoding.UTF8.GetBytes(originalTopic));
        var reason = exception.Message.Length > 1_000 ? exception.Message[..1_000] : exception.Message;
        headers.Add(FailureReasonHeader, Encoding.UTF8.GetBytes(reason));
        headers.Add("failed-at-utc", Encoding.UTF8.GetBytes(DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture)));

        await _producer.ProduceAsync(route.Topic, new Message<string, string>
        {
            Key = consumed.Message.Key,
            Value = consumed.Message.Value,
            Headers = headers
        }, cancellationToken);

        return route;
    }

    internal NotificationFailureRoute DetermineRoute(ConsumeResult<string, string> consumed)
    {
        var originalTopic = ReadHeader(consumed.Message.Headers, OriginalTopicHeader)
            ?? (consumed.Topic.EndsWith(".retry", StringComparison.Ordinal)
                ? consumed.Topic[..^".retry".Length]
                : consumed.Topic);
        var currentAttempt = int.TryParse(
            ReadHeader(consumed.Message.Headers, RetryAttemptHeader),
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out var parsedAttempt)
            ? parsedAttempt
            : 0;
        var nextAttempt = currentAttempt + 1;
        var isDeadLetter = nextAttempt >= _options.MaxDeliveryAttempts;
        return new NotificationFailureRoute(
            originalTopic + (isDeadLetter ? ".dlt" : ".retry"),
            nextAttempt,
            isDeadLetter);
    }

    private static Headers CopyHeaders(Headers? source)
    {
        var copied = new Headers();
        if (source is null) return copied;
        foreach (var header in source)
        {
            if (header.Key is RetryAttemptHeader or OriginalTopicHeader or FailureReasonHeader or "failed-at-utc") continue;
            copied.Add(header.Key, header.GetValueBytes());
        }

        return copied;
    }

    internal static string? ReadHeader(Headers? headers, string key)
    {
        var header = headers?.LastOrDefault(candidate =>
            string.Equals(candidate.Key, key, StringComparison.OrdinalIgnoreCase));
        return header is null ? null : Encoding.UTF8.GetString(header.GetValueBytes());
    }
}
