using MediCore.Patient.Application.DTOs;

namespace MediCore.Patient.Application.Interfaces;

public interface IPatientSearchRepository
{
    Task<PatientSearchResponse> SearchAsync(
        string searchTerm,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);
}
