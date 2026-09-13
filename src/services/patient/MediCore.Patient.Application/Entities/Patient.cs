namespace MediCore.Patient.Application.Entities;

public sealed class Patient : IAuditableEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    // Populated by PostgreSQL from the patient_number_seq default on INSERT.
    public string PatientNumber { get; set; } = null!;
    public string Nic { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public DateOnly DateOfBirth { get; set; }
    public string Gender { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string AddressLine1 { get; set; } = string.Empty;
    public string? AddressLine2 { get; set; }
    public string District { get; set; } = string.Empty;
    public string? EmergencyContactName { get; set; }
    public string? EmergencyContactPhone { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public string? UpdatedBy { get; set; }

    public string FullName => $"{FirstName} {LastName}".Trim();
}
