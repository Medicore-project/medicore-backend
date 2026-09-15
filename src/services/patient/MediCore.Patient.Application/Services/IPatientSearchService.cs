using MediCore.Patient.Application.DTOs;

namespace MediCore.Patient.Application.Services;

public interface IPatientSearchService
{
    Task<PatientSearchResponse> SearchAsync(
        PatientSearchRequest request,
        PatientAccessContext accessContext,
        CancellationToken cancellationToken = default);
}
