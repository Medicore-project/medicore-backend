namespace MediCore.Patient.Application.Entities;

public sealed class Condition : IAuditableEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid MedicalRecordId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Code { get; set; }
    public string ClinicalStatus { get; set; } = string.Empty;
    public string? Notes { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public string? UpdatedBy { get; set; }

    public MedicalRecord MedicalRecord { get; set; } = null!;
}
