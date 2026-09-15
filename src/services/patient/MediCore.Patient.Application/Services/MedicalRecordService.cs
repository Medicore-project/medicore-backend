using MediCore.Patient.Application.DTOs;
using MediCore.Patient.Application.Entities;
using MediCore.Patient.Application.Exceptions;
using MediCore.Patient.Application.Interfaces;
using ConditionEntity = MediCore.Patient.Application.Entities.Condition;
using MedicalRecordEntity = MediCore.Patient.Application.Entities.MedicalRecord;

namespace MediCore.Patient.Application.Services;

public sealed class MedicalRecordService : IMedicalRecordService
{
    private const int ClinicalNotesPreviewLength = 240;
    private readonly IPatientRepository _patientRepository;
    private readonly IMedicalRecordRepository _recordRepository;
    private readonly IPatientAuditRepository _auditRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _timeProvider;

    public MedicalRecordService(
        IPatientRepository patientRepository,
        IMedicalRecordRepository recordRepository,
        IPatientAuditRepository auditRepository,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider)
    {
        _patientRepository = patientRepository;
        _recordRepository = recordRepository;
        _auditRepository = auditRepository;
        _unitOfWork = unitOfWork;
        _timeProvider = timeProvider;
    }

    public async Task<PagedMedicalRecordResponse?> GetPageAsync(
        Guid patientId,
        MedicalRecordListRequest request,
        PatientAccessContext accessContext,
        CancellationToken cancellationToken = default)
    {
        if (await _patientRepository.GetByIdAsync(patientId, cancellationToken) is null)
        {
            return null;
        }

        var (items, totalCount) = await _recordRepository.GetCurrentPageAsync(
            patientId,
            request.Page,
            request.PageSize,
            cancellationToken);

        await AuditReadsAsync(items, accessContext, cancellationToken);

        var totalPages = totalCount == 0
            ? 0
            : (int)Math.Ceiling(totalCount / (double)request.PageSize);

        return new PagedMedicalRecordResponse(
            items.Select(ToSummaryResponse).ToList(),
            totalCount,
            request.Page,
            request.PageSize,
            totalPages,
            request.Page > 1 && totalCount > 0,
            request.Page < totalPages);
    }

    public async Task<MedicalRecordResponse?> GetByIdAsync(
        Guid patientId,
        Guid recordId,
        PatientAccessContext accessContext,
        CancellationToken cancellationToken = default)
    {
        var record = await _recordRepository.GetCurrentAsync(patientId, recordId, cancellationToken);
        if (record is null)
        {
            return null;
        }

        await AddAuditAsync(
            patientId,
            $"MedicalRecordViewed:{record.RecordId}:v{record.Version}",
            accessContext,
            cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return ToResponse(record);
    }

    public async Task<IReadOnlyList<MedicalRecordResponse>?> GetVersionsAsync(
        Guid patientId,
        Guid recordId,
        PatientAccessContext accessContext,
        CancellationToken cancellationToken = default)
    {
        if (await _recordRepository.GetCurrentAsync(patientId, recordId, cancellationToken) is null)
        {
            return null;
        }

        var versions = await _recordRepository.GetVersionsAsync(patientId, recordId, cancellationToken);
        await AuditReadsAsync(versions, accessContext, cancellationToken);
        return versions.Select(ToResponse).ToList();
    }

    public async Task<MedicalRecordCreateResult> CreateAsync(
        Guid patientId,
        CreateMedicalRecordRequest request,
        PatientAccessContext accessContext,
        CancellationToken cancellationToken = default)
    {
        if (await _patientRepository.GetByIdAsync(patientId, cancellationToken) is null)
        {
            return new MedicalRecordCreatePatientNotFoundResult();
        }

        var actorId = Normalize(accessContext.ActorId, "system");
        var record = new MedicalRecordEntity
        {
            PatientId = patientId,
            VisitReference = request.VisitReference,
            ClinicalNotes = request.ClinicalNotes.Trim(),
            AuthorClinicianId = actorId,
            AuthorClinicianEmail = NormalizeEmail(accessContext.ActorEmail, actorId),
            AuthorClinicianRole = Normalize(accessContext.ActorRole, "Unknown"),
            AuthoredAtUtc = _timeProvider.GetUtcNow().UtcDateTime,
            CreatedBy = actorId
        };
        record.Conditions = CreateConditionSnapshot(record.Id, request.Conditions, actorId);

        await _recordRepository.AddAsync(record, cancellationToken);
        await AddAuditAsync(
            patientId,
            $"MedicalRecordCreated:{record.RecordId}",
            accessContext,
            cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new MedicalRecordCreatedResult(ToResponse(record));
    }

    public async Task<MedicalRecordUpdateResult> UpdateAsync(
        Guid patientId,
        Guid recordId,
        UpdateMedicalRecordRequest request,
        PatientAccessContext accessContext,
        CancellationToken cancellationToken = default)
    {
        var current = await _recordRepository.GetTrackedCurrentAsync(
            patientId,
            recordId,
            cancellationToken);
        if (current is null)
        {
            return new MedicalRecordUpdateNotFoundResult();
        }

        if (current.Version != request.ExpectedVersion)
        {
            return new MedicalRecordUpdateConflictResult(current.Version);
        }

        var actorId = Normalize(accessContext.ActorId, "system");
        current.IsCurrent = false;
        current.UpdatedBy = actorId;

        var nextVersion = new MedicalRecordEntity
        {
            RecordId = current.RecordId,
            PatientId = current.PatientId,
            VisitReference = current.VisitReference,
            ClinicalNotes = request.ClinicalNotes.Trim(),
            AuthorClinicianId = actorId,
            AuthorClinicianEmail = NormalizeEmail(accessContext.ActorEmail, actorId),
            AuthorClinicianRole = Normalize(accessContext.ActorRole, "Unknown"),
            AuthoredAtUtc = _timeProvider.GetUtcNow().UtcDateTime,
            Version = current.Version + 1,
            PreviousVersionId = current.Id,
            CreatedBy = actorId
        };
        nextVersion.Conditions = CreateConditionSnapshot(nextVersion.Id, request.Conditions, actorId);

        await _recordRepository.AddAsync(nextVersion, cancellationToken);
        await AddAuditAsync(
            patientId,
            $"MedicalRecordUpdated:{recordId}:v{current.Version}-v{nextVersion.Version}",
            accessContext,
            cancellationToken);

        try
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (MedicalRecordVersionConflictException)
        {
            return new MedicalRecordUpdateConflictResult(current.Version + 1);
        }

        return new MedicalRecordUpdatedResult(ToResponse(nextVersion));
    }

    public async Task<bool> DeleteAsync(
        Guid patientId,
        Guid recordId,
        PatientAccessContext accessContext,
        CancellationToken cancellationToken = default)
    {
        var record = await _recordRepository.GetTrackedCurrentAsync(
            patientId,
            recordId,
            cancellationToken);
        if (record is null)
        {
            return false;
        }

        record.IsCurrent = false;
        record.IsDeleted = true;
        record.UpdatedBy = Normalize(accessContext.ActorId, "system");

        await AddAuditAsync(
            patientId,
            $"MedicalRecordDeleted:{recordId}:v{record.Version}",
            accessContext,
            cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return true;
    }

    private async Task AuditReadsAsync(
        IReadOnlyList<MedicalRecordEntity> records,
        PatientAccessContext accessContext,
        CancellationToken cancellationToken)
    {
        foreach (var record in records)
        {
            await AddAuditAsync(
                record.PatientId,
                $"MedicalRecordViewed:{record.RecordId}:v{record.Version}",
                accessContext,
                cancellationToken);
        }

        if (records.Count > 0)
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
    }

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

    private static List<ConditionEntity> CreateConditionSnapshot(
        Guid medicalRecordId,
        IReadOnlyList<ConditionRequest> conditions,
        string actorId) =>
        conditions.Select(condition => new ConditionEntity
        {
            MedicalRecordId = medicalRecordId,
            Name = condition.Name.Trim(),
            Code = Optional(condition.Code),
            ClinicalStatus = CanonicalStatus(condition.ClinicalStatus),
            Notes = Optional(condition.Notes),
            CreatedBy = actorId
        }).ToList();

    private static MedicalRecordSummaryResponse ToSummaryResponse(MedicalRecordEntity record) => new(
        record.RecordId,
        record.Id,
        record.PatientId,
        record.VisitReference,
        CreateClinicalNotesPreview(record.ClinicalNotes),
        record.Conditions.Count,
        record.AuthorClinicianId,
        record.AuthorClinicianEmail,
        record.AuthorClinicianRole,
        record.AuthoredAtUtc,
        record.Version);

    private static string CreateClinicalNotesPreview(string clinicalNotes)
    {
        var normalized = string.Join(
            " ",
            clinicalNotes.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

        return normalized.Length <= ClinicalNotesPreviewLength
            ? normalized
            : $"{normalized[..ClinicalNotesPreviewLength].TrimEnd()}…";
    }

    private static MedicalRecordResponse ToResponse(MedicalRecordEntity record) => new(
        record.RecordId,
        record.Id,
        record.PatientId,
        record.VisitReference,
        record.ClinicalNotes,
        record.AuthorClinicianId,
        record.AuthorClinicianEmail,
        record.AuthorClinicianRole,
        record.AuthoredAtUtc,
        record.Version,
        record.PreviousVersionId,
        record.IsCurrent,
        record.Conditions
            .OrderBy(condition => condition.Name)
            .Select(condition => new ConditionResponse(
                condition.Id,
                condition.Name,
                condition.Code,
                condition.ClinicalStatus,
                condition.Notes))
            .ToList());

    private static string CanonicalStatus(string status) => status.Trim().ToLowerInvariant() switch
    {
        "active" => "Active",
        "resolved" => "Resolved",
        "historical" => "Historical",
        _ => status.Trim()
    };

    private static string? Optional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string Normalize(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    private static string NormalizeEmail(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim().ToLowerInvariant();
}
