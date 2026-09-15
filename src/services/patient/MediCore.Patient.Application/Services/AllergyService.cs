using MediCore.Patient.Application.DTOs;
using MediCore.Patient.Application.Entities;
using MediCore.Patient.Application.Interfaces;
using AllergyEntity = MediCore.Patient.Application.Entities.Allergy;

namespace MediCore.Patient.Application.Services;

/// <summary>
/// Application-layer service for the Allergy aggregate.
/// Enforces business rules (status guards, audit trail) and delegates
/// persistence to <see cref="IAllergyRepository"/> + <see cref="IUnitOfWork"/>.
/// </summary>
public sealed class AllergyService : IAllergyService
{
    private readonly IPatientRepository _patientRepository;
    private readonly IAllergyRepository _allergyRepository;
    private readonly IPatientAuditRepository _auditRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _timeProvider;

    public AllergyService(
        IPatientRepository patientRepository,
        IAllergyRepository allergyRepository,
        IPatientAuditRepository auditRepository,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider)
    {
        _patientRepository = patientRepository;
        _allergyRepository = allergyRepository;
        _auditRepository = auditRepository;
        _unitOfWork = unitOfWork;
        _timeProvider = timeProvider;
    }

    // ── Queries ───────────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<AllergyResponse>?> GetListAsync(
        Guid patientId,
        PatientAccessContext accessContext,
        CancellationToken cancellationToken = default)
    {
        if (await _patientRepository.GetByIdAsync(patientId, cancellationToken) is null)
        {
            return null;
        }

        var allergies = await _allergyRepository.GetAllAsync(patientId, cancellationToken);

        await AddAuditAsync(patientId, "AllergiesListed", accessContext, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return allergies.Select(ToResponse).ToList();
    }

    public async Task<AllergyResponse?> GetByIdAsync(
        Guid patientId,
        Guid allergyId,
        PatientAccessContext accessContext,
        CancellationToken cancellationToken = default)
    {
        var allergy = await _allergyRepository.GetByIdAsync(patientId, allergyId, cancellationToken);
        if (allergy is null)
        {
            return null;
        }

        await AddAuditAsync(
            patientId, $"AllergyViewed:{allergyId}", accessContext, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return ToResponse(allergy);
    }

    public async Task<AllergyConflictResponse> CheckConflictAsync(
        Guid patientId,
        string drug,
        CancellationToken cancellationToken = default)
    {
        var match = await _allergyRepository.CheckConflictAsync(patientId, drug, cancellationToken);
        return match is null
            ? new AllergyConflictResponse(false, null)
            : new AllergyConflictResponse(true, ToResponse(match));
    }

    // ── Commands ──────────────────────────────────────────────────────────────

    public async Task<AllergyCreateResult> CreateAsync(
        Guid patientId,
        CreateAllergyRequest request,
        PatientAccessContext accessContext,
        CancellationToken cancellationToken = default)
    {
        if (await _patientRepository.GetByIdAsync(patientId, cancellationToken) is null)
        {
            return new AllergyCreatePatientNotFoundResult();
        }

        var actorId = Normalize(accessContext.ActorId, "system");
        var utcNow = _timeProvider.GetUtcNow().UtcDateTime;

        var allergy = new AllergyEntity
        {
            PatientId = patientId,
            Allergen = request.Allergen.Trim(),
            Severity = request.Severity.Trim(),
            Reaction = Optional(request.Reaction),
            Notes = Optional(request.Notes),
            Status = AllergyStatus.Active,
            RecordedAtUtc = utcNow,
            RecordedByClinicianId = actorId,
            RecordedByClinicianEmail = NormalizeEmail(accessContext.ActorEmail, actorId),
            RecordedByClinicianRole = Normalize(accessContext.ActorRole, "Unknown"),
            CreatedBy = actorId
        };

        await _allergyRepository.AddAsync(allergy, cancellationToken);
        await AddAuditAsync(
            patientId, $"AllergyRecorded:{allergy.AllergyId}", accessContext, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new AllergyCreatedResult(ToResponse(allergy));
    }

    public async Task<AllergyUpdateResult> UpdateAsync(
        Guid patientId,
        Guid allergyId,
        UpdateAllergyRequest request,
        PatientAccessContext accessContext,
        CancellationToken cancellationToken = default)
    {
        var allergy = await _allergyRepository.GetTrackedByIdAsync(patientId, allergyId, cancellationToken);
        if (allergy is null)
        {
            return new AllergyUpdateNotFoundResult();
        }

        var actorId = Normalize(accessContext.ActorId, "system");
        allergy.Allergen = request.Allergen.Trim();
        allergy.Severity = request.Severity.Trim();
        allergy.Reaction = Optional(request.Reaction);
        allergy.Notes = Optional(request.Notes);
        allergy.UpdatedBy = actorId;

        await AddAuditAsync(
            patientId, $"AllergyUpdated:{allergyId}", accessContext, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new AllergyUpdatedResult(ToResponse(allergy));
    }

    public async Task<AllergyDeactivateResult> DeactivateAsync(
        Guid patientId,
        Guid allergyId,
        PatientAccessContext accessContext,
        CancellationToken cancellationToken = default)
    {
        var allergy = await _allergyRepository.GetTrackedByIdAsync(patientId, allergyId, cancellationToken);
        if (allergy is null)
        {
            return new AllergyDeactivateNotFoundResult();
        }

        if (allergy.Status == AllergyStatus.Inactive)
        {
            return new AllergyDeactivateAlreadyInactiveResult();
        }

        var actorId = Normalize(accessContext.ActorId, "system");
        allergy.Status = AllergyStatus.Inactive;
        allergy.UpdatedBy = actorId;

        await AddAuditAsync(
            patientId, $"AllergyDeactivated:{allergyId}", accessContext, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new AllergyDeactivatedResult(ToResponse(allergy));
    }

    public async Task<bool> DeleteAsync(
        Guid patientId,
        Guid allergyId,
        PatientAccessContext accessContext,
        CancellationToken cancellationToken = default)
    {
        var allergy = await _allergyRepository.GetTrackedByIdAsync(patientId, allergyId, cancellationToken);
        if (allergy is null)
        {
            return false;
        }

        var actorId = Normalize(accessContext.ActorId, "system");
        allergy.IsDeleted = true;
        allergy.UpdatedBy = actorId;

        await AddAuditAsync(
            patientId, $"AllergyDeleted:{allergyId}", accessContext, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return true;
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private Task AddAuditAsync(
        Guid patientId,
        string action,
        PatientAccessContext accessContext,
        CancellationToken cancellationToken) =>
        _auditRepository.AddAsync(new PatientAuditLog
        {
            PatientId = patientId,
            ActorId = Normalize(accessContext.ActorId, "system"),
            ActorRole = Normalize(accessContext.ActorRole, "Unknown"),
            Action = action,
            CorrelationId = accessContext.CorrelationId,
            IpAddress = string.IsNullOrWhiteSpace(accessContext.IpAddress)
                ? null
                : accessContext.IpAddress.Trim(),
            OccurredAtUtc = _timeProvider.GetUtcNow().UtcDateTime
        }, cancellationToken);

    private static AllergyResponse ToResponse(AllergyEntity a) => new(
        a.AllergyId,
        a.PatientId,
        a.Allergen,
        a.Severity,
        a.Reaction,
        a.Status,
        a.RecordedAtUtc,
        a.Notes,
        a.RecordedByClinicianId,
        a.RecordedByClinicianEmail,
        a.RecordedByClinicianRole);

    private static string? Optional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string Normalize(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    private static string NormalizeEmail(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim().ToLowerInvariant();
}
