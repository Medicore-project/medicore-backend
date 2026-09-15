namespace MediCore.Patient.Application.Entities;

public sealed class ProcessedMessage
{
    public Guid MessageId { get; set; }
    public string EventType { get; set; } = string.Empty;
    public string ConsumerGroup { get; set; } = string.Empty;
    public string SourceTopic { get; set; } = string.Empty;
    public DateTime ProcessedAtUtc { get; set; }
}
