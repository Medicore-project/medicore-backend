using MediCore.Patient.Application.DTOs;
using MediCore.Patient.Application.Entities;
using MediCore.Patient.Application.Interfaces;

namespace MediCore.Patient.Application.Services;

public sealed class PatientSearchService : IPatientSearchService
{
    private readonly IPatientSearchRepository _searchRepository;
    private readonly IPatientAuditRepository _auditRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _timeProvider;

    public PatientSearchService(
        IPatientSearchRepository searchRepository,
        IPatientAuditRepository auditRepository,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider)
    {
        _searchRepository = searchRepository;
        _auditRepository = auditRepository;
        _unitOfWork = unitOfWork;
        _timeProvider = timeProvider;
    }

    public async Task<PatientSearchResponse> SearchAsync(
        PatientSearchRequest request,
        PatientAccessContext accessContext,
        CancellationToken cancellationToken = default)
    {
        var searchTerm = request.Q?.Trim() ?? string.Empty;

        var result = await _searchRepository.SearchAsync(
            searchTerm,
            request.Page,
            request.PageSize,
            cancellationToken);

        if (result.Items.Count == 0)
        {
            return result;
        }

        var occurredAtUtc = _timeProvider.GetUtcNow().UtcDateTime;
        foreach (var patient in result.Items)
        {
            await _auditRepository.AddAsync(new PatientAuditLog
            {
                PatientId = patient.PatientId,
                ActorId = Normalize(accessContext.ActorId, "system"),
                ActorRole = Normalize(accessContext.ActorRole, "Unknown"),
                Action = "PatientSearchResultViewed",
                CorrelationId = accessContext.CorrelationId,
                IpAddress = string.IsNullOrWhiteSpace(accessContext.IpAddress)
                    ? null
                    : accessContext.IpAddress.Trim(),
                OccurredAtUtc = occurredAtUtc
            }, cancellationToken);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return result;
    }

    private static string Normalize(string value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
}
