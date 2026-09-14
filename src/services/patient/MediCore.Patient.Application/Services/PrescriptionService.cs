using MediCore.Patient.Application.DTOs;
using MediCore.Patient.Application.Entities;
using MediCore.Patient.Application.Interfaces;
using PrescriptionEntity = MediCore.Patient.Application.Entities.Prescription;

namespace MediCore.Patient.Application.Services;

/// <summary>
/// Application-layer service for the Prescription aggregate.
/// Enforces business rules (status guards, audit trail) and delegates
/// persistence to <see cref="IPrescriptionRepository"/> + <see cref="IUnitOfWork"/>.
/// </summary>
public sealed class PrescriptionService : IPrescriptionService
{
    private readonly IPatientRepository _patientRepository;
    private readonly IPrescriptionRepository _prescriptionRepository;
    private readonly IPatientAuditRepository _auditRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _timeProvider;

    public PrescriptionService(
        IPatientRepository patientRepository,
        IPrescriptionRepository prescriptionRepository,
        IPatientAuditRepository auditRepository,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider)
    {
        _patientRepository = patientRepository;
        _prescriptionRepository = prescriptionRepository;
        _auditRepository = auditRepository;
        _unitOfWork = unitOfWork;
        _timeProvider = timeProvider;
    }

    // ── Queries ───────────────────────────────────────────────────────────────

    public async Task<PrescriptionListResponse?> GetListAsync(
        Guid patientId,
        PatientAccessContext accessContext,
        CancellationToken cancellationToken = default)
    {
        if (await _patientRepository.GetByIdAsync(patientId, cancellationToken) is null)
        {
            return null;
        }

        var active = await _prescriptionRepository.GetActiveAsync(patientId, cancellationToken);
        var history = await _prescriptionRepository.GetHistoryAsync(patientId, cancellationToken);

        await AddAuditAsync(patientId, "PrescriptionsListed", accessContext, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new PrescriptionListResponse(
            active.Select(ToResponse).ToList(),
            history.Select(ToResponse).ToList());
    }

    public async Task<PrescriptionResponse?> GetByIdAsync(
        Guid patientId,
        Guid prescriptionId,
        PatientAccessContext accessContext,
        CancellationToken cancellationToken = default)
    {
        var prescription = await _prescriptionRepository.GetByIdAsync(patientId, prescriptionId, cancellationToken);
        if (prescription is null)
        {
            return null;
        }

        await AddAuditAsync(
            patientId,
            $"PrescriptionViewed:{prescriptionId}",
            accessContext,
            cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return ToResponse(prescription);
    }

    // ── Commands ──────────────────────────────────────────────────────────────

    public async Task<PrescriptionCreateResult> CreateAsync(
        Guid patientId,
        CreatePrescriptionRequest request,
        PatientAccessContext accessContext,
        CancellationToken cancellationToken = default)
    {
        if (await _patientRepository.GetByIdAsync(patientId, cancellationToken) is null)
        {
            return new PrescriptionCreatePatientNotFoundResult();
        }

        var actorId = Normalize(accessContext.ActorId, "system");
        var utcNow = _timeProvider.GetUtcNow().UtcDateTime;

        var prescription = new PrescriptionEntity
        {
            PatientId = patientId,
            MedicalRecordId = request.MedicalRecordId,
            Drug = request.Drug.Trim(),
            Dosage = request.Dosage.Trim(),
            Frequency = request.Frequency.Trim(),
            DurationDays = request.DurationDays,
            Notes = Optional(request.Notes),
            Status = PrescriptionStatus.Active,
            PrescriberClinicianId = actorId,
            PrescriberClinicianEmail = NormalizeEmail(accessContext.ActorEmail, actorId),
            PrescriberClinicianRole = Normalize(accessContext.ActorRole, "Unknown"),
            PrescribedAtUtc = utcNow,
            CreatedBy = actorId
        };

        await _prescriptionRepository.AddAsync(prescription, cancellationToken);
        await AddAuditAsync(
            patientId,
            $"PrescriptionCreated:{prescription.PrescriptionId}",
            accessContext,
            cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new PrescriptionCreatedResult(ToResponse(prescription));
    }

    public async Task<PrescriptionUpdateResult> UpdateAsync(
        Guid patientId,
        Guid prescriptionId,
        UpdatePrescriptionRequest request,
        PatientAccessContext accessContext,
        CancellationToken cancellationToken = default)
    {
        var prescription = await _prescriptionRepository.GetTrackedByIdAsync(
            patientId, prescriptionId, cancellationToken);

        // Also returns not-found for completed prescriptions (they are
        // intentionally immutable once moved to history).
        if (prescription is null || prescription.Status != PrescriptionStatus.Active)
        {
            return new PrescriptionUpdateNotFoundResult();
        }

        var actorId = Normalize(accessContext.ActorId, "system");
        prescription.Drug = request.Drug.Trim();
        prescription.Dosage = request.Dosage.Trim();
        prescription.Frequency = request.Frequency.Trim();
        prescription.DurationDays = request.DurationDays;
        prescription.Notes = Optional(request.Notes);
        prescription.UpdatedBy = actorId;

        await AddAuditAsync(
            patientId,
            $"PrescriptionUpdated:{prescriptionId}",
            accessContext,
            cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new PrescriptionUpdatedResult(ToResponse(prescription));
    }

    public async Task<PrescriptionCompleteResult> CompleteAsync(
        Guid patientId,
        Guid prescriptionId,
        PatientAccessContext accessContext,
        CancellationToken cancellationToken = default)
    {
        var prescription = await _prescriptionRepository.GetTrackedByIdAsync(
            patientId, prescriptionId, cancellationToken);

        if (prescription is null)
        {
            return new PrescriptionCompleteNotFoundResult();
        }

        if (prescription.Status == PrescriptionStatus.Completed)
        {
            return new PrescriptionCompleteAlreadyDoneResult();
        }

        var actorId = Normalize(accessContext.ActorId, "system");
        var utcNow = _timeProvider.GetUtcNow().UtcDateTime;

        prescription.Status = PrescriptionStatus.Completed;
        prescription.CompletedAtUtc = utcNow;
        prescription.UpdatedBy = actorId;

        await AddAuditAsync(
            patientId,
            $"PrescriptionCompleted:{prescriptionId}",
            accessContext,
            cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new PrescriptionCompletedResult(ToResponse(prescription));
    }

    public async Task<bool> DeleteAsync(
        Guid patientId,
        Guid prescriptionId,
        PatientAccessContext accessContext,
        CancellationToken cancellationToken = default)
    {
        var prescription = await _prescriptionRepository.GetTrackedByIdAsync(
            patientId, prescriptionId, cancellationToken);

        if (prescription is null)
        {
            return false;
        }

        var actorId = Normalize(accessContext.ActorId, "system");
        prescription.IsDeleted = true;
        prescription.UpdatedBy = actorId;

        await AddAuditAsync(
            patientId,
            $"PrescriptionDeleted:{prescriptionId}",
            accessContext,
            cancellationToken);
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

    private static PrescriptionResponse ToResponse(PrescriptionEntity p) => new(
        p.PrescriptionId,
        p.PatientId,
        p.MedicalRecordId,
        p.Drug,
        p.Dosage,
        p.Frequency,
        p.DurationDays,
        p.Status,
        p.PrescriberClinicianId,
        p.PrescriberClinicianEmail,
        p.PrescriberClinicianRole,
        p.PrescribedAtUtc,
        p.CompletedAtUtc,
        p.Notes);

    private static string? Optional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string Normalize(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    private static string NormalizeEmail(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim().ToLowerInvariant();
}
