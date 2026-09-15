namespace MediCore.Patient.Application.Entities;

public sealed class MedicalRecord : IAuditableEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RecordId { get; set; } = Guid.NewGuid();
    public Guid PatientId { get; set; }
    public Guid VisitReference { get; set; }
    public string ClinicalNotes { get; set; } = string.Empty;
    public string AuthorClinicianId { get; set; } = string.Empty;
    public string AuthorClinicianEmail { get; set; } = string.Empty;
    public string AuthorClinicianRole { get; set; } = string.Empty;
    public DateTime AuthoredAtUtc { get; set; }
    public int Version { get; set; } = 1;
    public Guid? PreviousVersionId { get; set; }
    public bool IsCurrent { get; set; } = true;
    public bool IsDeleted { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public string? UpdatedBy { get; set; }

    public MedicalRecord? PreviousVersion { get; set; }
    public ICollection<Condition> Conditions { get; set; } = [];
}
