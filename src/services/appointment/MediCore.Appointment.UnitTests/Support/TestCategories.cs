namespace MediCore.Appointment.UnitTests.Support;

/// <summary>
/// Trait names and values used on every test class, so the suite can be sliced from the
/// command line or the pipeline:
/// <code>
/// dotnet test --filter "Category=Unit"
/// dotnet test --filter "Technique=Parameterized"
/// dotnet test --filter "Feature=Booking"
/// </code>
/// </summary>
public static class TestCategories
{
    public const string Category = "Category";
    public const string Unit = "Unit";

    public const string Technique = "Technique";
    public const string Parameterized = "Parameterized";
    public const string Mocking = "Mocking";

    public const string Feature = "Feature";
    public const string Booking = "Booking";
    public const string Lifecycle = "Lifecycle";
    public const string Waitlist = "Waitlist";
    public const string Validation = "Validation";
    public const string DomainRules = "DomainRules";
}
