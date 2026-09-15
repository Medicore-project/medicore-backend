namespace MediCore.Patient.Application.DTOs;

public sealed class PatientSearchRequest
{
    public PatientSearchRequest()
    {
    }

    public PatientSearchRequest(string? q, int page = 1, int pageSize = 20)
    {
        Q = q;
        Page = page;
        PageSize = pageSize;
    }

    public string? Q { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 20;
}

public sealed record PatientSearchResult(
    Guid PatientId,
    string PatientNumber,
    string Nic,
    string FullName,
    DateOnly DateOfBirth,
    string Phone,
    string Email,
    string District);

public sealed record PatientSearchResponse(
    IReadOnlyList<PatientSearchResult> Items,
    int TotalCount,
    int Page,
    int PageSize,
    int TotalPages,
    bool HasPreviousPage,
    bool HasNextPage)
{
    public static PatientSearchResponse Empty(int page, int pageSize) =>
        new([], 0, page, pageSize, 0, false, false);
}
