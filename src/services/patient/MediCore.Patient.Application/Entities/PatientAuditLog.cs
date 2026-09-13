namespace MediCore.Patient.Application.Entities;

public sealed class PatientAuditLog
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PatientId { get; set; }
    public string ActorId { get; set; } = string.Empty;
    public string ActorRole { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string CorrelationId { get; set; } = string.Empty;
    public string? IpAddress { get; set; }
    public DateTime OccurredAtUtc { get; set; }
}
