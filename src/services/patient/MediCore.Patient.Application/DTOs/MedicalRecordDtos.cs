namespace MediCore.Patient.Application.DTOs;

public sealed record ConditionRequest(
    string Name,
    string? Code,
    string ClinicalStatus,
    string? Notes);

public sealed record ConditionResponse(
    Guid ConditionId,
    string Name,
    string? Code,
    string ClinicalStatus,
    string? Notes);

public sealed record CreateMedicalRecordRequest(
    Guid VisitReference,
    string ClinicalNotes,
    IReadOnlyList<ConditionRequest> Conditions);

public sealed record UpdateMedicalRecordRequest(
    int ExpectedVersion,
    string ClinicalNotes,
    IReadOnlyList<ConditionRequest> Conditions);

public sealed class MedicalRecordListRequest
{
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 20;
}

public sealed record MedicalRecordSummaryResponse(
    Guid RecordId,
    Guid VersionId,
    Guid PatientId,
    Guid VisitReference,
    string AuthorClinicianId,
    string AuthorClinicianEmail,
    string AuthorClinicianRole,
    DateTime AuthoredAtUtc,
    int Version);

public sealed record MedicalRecordResponse(
    Guid RecordId,
    Guid VersionId,
    Guid PatientId,
    Guid VisitReference,
    string ClinicalNotes,
    string AuthorClinicianId,
    string AuthorClinicianEmail,
    string AuthorClinicianRole,
    DateTime AuthoredAtUtc,
    int Version,
    Guid? PreviousVersionId,
    bool IsCurrent,
    IReadOnlyList<ConditionResponse> Conditions);

public sealed record PagedMedicalRecordResponse(
    IReadOnlyList<MedicalRecordSummaryResponse> Items,
    int TotalCount,
    int Page,
    int PageSize,
    int TotalPages,
    bool HasPreviousPage,
    bool HasNextPage);
