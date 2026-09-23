using MediCore.Patient.Application.Interfaces;
using MediCore.Patient.Application.Services;
using PatientEntity = MediCore.Patient.Application.Entities.Patient;

namespace MediCore.Patient.Tests.Unit;

public sealed class PatientIdentificationServiceTests
{
    private static readonly Guid PatientId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly DateOnly DateOfBirth = new(1995, 4, 2);

    [Fact]
    public async Task A_matching_number_and_date_of_birth_identifies_the_patient()
    {
        var fixture = new Fixture(Patient());

        var identity = await fixture.Service.IdentifyAsync("PAT-000123", DateOfBirth);

        Assert.NotNull(identity);
        Assert.Equal(PatientId, identity.PatientId);
        Assert.Equal("PAT-000123", identity.PatientNumber);
        Assert.Equal("Nimal Perera", identity.FullName);
        Assert.Equal("token-for-" + PatientId, identity.BookingToken);
    }

    [Fact]
    public async Task A_number_nobody_holds_identifies_nobody()
    {
        var fixture = new Fixture(patient: null);

        Assert.Null(await fixture.Service.IdentifyAsync("PAT-999999", DateOfBirth));
    }

    [Fact]
    public async Task The_wrong_date_of_birth_identifies_nobody()
    {
        var fixture = new Fixture(Patient());

        Assert.Null(await fixture.Service.IdentifyAsync("PAT-000123", new DateOnly(1980, 1, 1)));
    }

    [Fact]
    public async Task Both_failures_take_the_same_path_so_neither_can_be_told_from_the_other()
    {
        // Patient numbers are sequential, so "that number exists but the date is wrong" would be
        // a confirmation oracle. One query, one outcome, no branch that could leak the difference.
        var unknownNumber = new Fixture(Patient());
        var wrongDate = new Fixture(Patient());

        await unknownNumber.Service.IdentifyAsync("PAT-999999", DateOfBirth);
        await wrongDate.Service.IdentifyAsync("PAT-000123", new DateOnly(1980, 1, 1));

        Assert.Equal(1, unknownNumber.Patients.QueryCount);
        Assert.Equal(1, wrongDate.Patients.QueryCount);
        // And no token was minted on either path.
        Assert.Equal(0, unknownNumber.Tokens.GenerateCount);
        Assert.Equal(0, wrongDate.Tokens.GenerateCount);
    }

    [Theory]
    [InlineData("pat-000123")]
    [InlineData("  PAT-000123  ")]
    public async Task A_number_read_off_a_card_is_normalised_before_the_lookup(string typed)
    {
        var fixture = new Fixture(Patient());

        var identity = await fixture.Service.IdentifyAsync(typed, DateOfBirth);

        Assert.NotNull(identity);
        Assert.Equal("PAT-000123", fixture.Patients.LastQueriedNumber);
    }

    [Fact]
    public void A_newly_registered_patient_gets_a_token_without_identifying_again()
    {
        var fixture = new Fixture(patient: null);

        var identity = fixture.Service.IssueFor(PatientId, "PAT-000456", "Kamala Silva");

        Assert.Equal(PatientId, identity.PatientId);
        Assert.Equal("PAT-000456", identity.PatientNumber);
        Assert.Equal("Kamala Silva", identity.FullName);
        Assert.Equal("token-for-" + PatientId, identity.BookingToken);
    }

    private static PatientEntity Patient() => new()
    {
        Id = PatientId,
        PatientNumber = "PAT-000123",
        Nic = "199504201234",
        FirstName = "Nimal",
        LastName = "Perera",
        DateOfBirth = DateOfBirth,
        Gender = "Male",
        Email = "nimal@example.com",
        Phone = "0771234567",
        AddressLine1 = "1 Galle Road",
        District = "Colombo"
    };

    private sealed class Fixture
    {
        public Fixture(PatientEntity? patient)
        {
            Patients = new FakePatientRepository(patient);
            Tokens = new FakeBookingTokenGenerator();
            Service = new PatientIdentificationService(Patients, Tokens);
        }

        public FakePatientRepository Patients { get; }

        public FakeBookingTokenGenerator Tokens { get; }

        public PatientIdentificationService Service { get; }
    }

    private sealed class FakePatientRepository : IPatientRepository
    {
        private readonly PatientEntity? _patient;

        public FakePatientRepository(PatientEntity? patient)
        {
            _patient = patient;
        }

        public int QueryCount { get; private set; }

        public string? LastQueriedNumber { get; private set; }

        public Task<PatientEntity?> FindByPatientNumberAndDateOfBirthAsync(
            string normalizedPatientNumber,
            DateOnly dateOfBirth,
            CancellationToken cancellationToken = default)
        {
            QueryCount++;
            LastQueriedNumber = normalizedPatientNumber;

            // The real query matches on both columns at once, as the database does.
            return Task.FromResult(
                _patient is not null
                && _patient.PatientNumber == normalizedPatientNumber
                && _patient.DateOfBirth == dateOfBirth
                    ? _patient
                    : null);
        }

        public Task<PatientEntity?> FindByNicAsync(
            string normalizedNic, bool includeArchived, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Identification never looks up by NIC.");

        public Task AddAsync(PatientEntity patient, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Identification never creates a patient.");

        public Task<PatientEntity?> GetByIdAsync(Guid patientId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Identification works from a number, not an id.");

        public Task<PatientEntity?> GetTrackedByIdAsync(Guid patientId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Identification never mutates a patient.");
    }

    private sealed class FakeBookingTokenGenerator : IBookingTokenGenerator
    {
        public int GenerateCount { get; private set; }

        public (string Token, DateTime ExpiresAtUtc) Generate(Guid patientId)
        {
            GenerateCount++;
            return ("token-for-" + patientId, new DateTime(2026, 9, 23, 8, 20, 0, DateTimeKind.Utc));
        }
    }
}
