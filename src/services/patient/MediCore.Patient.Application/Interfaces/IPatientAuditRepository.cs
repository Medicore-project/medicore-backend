using MediCore.Patient.Application.Entities;

namespace MediCore.Patient.Application.Interfaces;

public interface IPatientAuditRepository
{
    Task AddAsync(PatientAuditLog auditLog, CancellationToken cancellationToken = default);
}
