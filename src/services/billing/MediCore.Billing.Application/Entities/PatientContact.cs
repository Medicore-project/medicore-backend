namespace MediCore.Billing.Application.Entities;

public sealed class PatientContact
{
    public Guid PatientId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public DateTime UpdatedAtUtc { get; set; }
}
