namespace MediCore.Patient.Application.Services;

internal static class PatientInputNormalizer
{
    public static string Nic(string nic) => nic.Trim().ToUpperInvariant();

    /// <summary>
    /// Patient numbers are generated as <c>PAT-000123</c>. Someone reading theirs off a card will
    /// type it however it looks to them, so case and surrounding space are not a reason to fail
    /// to find them.
    /// </summary>
    public static string PatientNumber(string patientNumber) =>
        patientNumber.Trim().ToUpperInvariant();

    public static string Phone(string phone) =>
        phone.Replace(" ", string.Empty)
            .Replace("-", string.Empty)
            .Replace("(", string.Empty)
            .Replace(")", string.Empty)
            .Trim();

    public static string? OptionalPhone(string? phone) =>
        string.IsNullOrWhiteSpace(phone) ? null : Phone(phone);

    public static string? OptionalText(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    public static string Gender(string gender) => gender.Trim().ToLowerInvariant() switch
    {
        "male" => "Male",
        "female" => "Female",
        "other" => "Other",
        "prefer not to say" or "prefernottosay" => "PreferNotToSay",
        _ => gender.Trim()
    };
}
