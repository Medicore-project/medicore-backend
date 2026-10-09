using MediCore.Billing.Application.Entities;

namespace MediCore.Billing.Application.Interfaces;

public interface IPatientContactRepository
{
    Task<PatientContact?> GetAsync(Guid patientId, CancellationToken cancellationToken = default);
    Task AddAsync(PatientContact contact, CancellationToken cancellationToken = default);
}
