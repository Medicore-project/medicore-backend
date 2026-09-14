namespace MediCore.Patient.Application.DTOs;

public sealed class DemographicsReportFilter
{
    public DemographicsReportFilter()
    {
    }

    public DemographicsReportFilter(
        string? ageBand,
        string? gender,
        string? district,
        DateOnly? from,
        DateOnly? to)
    {
        AgeBand = ageBand;
        Gender = gender;
        District = district;
        From = from;
        To = to;
    }

    public string? AgeBand { get; init; }
    public string? Gender { get; init; }
    public string? District { get; init; }
    public DateOnly? From { get; init; }
    public DateOnly? To { get; init; }
}

public sealed record DemographicsReportSourceRow(
    Guid PatientId,
    string PatientNumber,
    int Age,
    string AgeBand,
    string Gender,
    string District,
    Guid? VisitRecordId,
    Guid? VisitReference,
    DateTime? VisitAtUtc);

public readonly record struct DemographicsAgeRange(int MinimumAge, int? MaximumAge);

public static class DemographicsReportOptions
{
    public const string Age0To17 = "0-17";
    public const string Age18To34 = "18-34";
    public const string Age35To49 = "35-49";
    public const string Age50To64 = "50-64";
    public const string Age65Plus = "65+";

    private static readonly IReadOnlyList<string> AgeBandValues = Array.AsReadOnly(
        new[] { Age0To17, Age18To34, Age35To49, Age50To64, Age65Plus });

    private static readonly IReadOnlyList<string> GenderValues = Array.AsReadOnly(
        new[] { "Male", "Female", "Other", "PreferNotToSay" });

    public static IReadOnlyList<string> AgeBands => AgeBandValues;
    public static IReadOnlyList<string> Genders => GenderValues;

    public static bool TryGetAgeRange(string? value, out DemographicsAgeRange range)
    {
        switch (value?.Trim())
        {
            case Age0To17:
                range = new(0, 17);
                return true;
            case Age18To34:
                range = new(18, 34);
                return true;
            case Age35To49:
                range = new(35, 49);
                return true;
            case Age50To64:
                range = new(50, 64);
                return true;
            case Age65Plus:
                range = new(65, null);
                return true;
            default:
                range = default;
                return false;
        }
    }

    public static string NormalizeGender(string value) => value.Trim().ToLowerInvariant() switch
    {
        "male" => "Male",
        "female" => "Female",
        "other" => "Other",
        "prefer not to say" or "prefernottosay" => "PreferNotToSay",
        _ => value.Trim()
    };
}
