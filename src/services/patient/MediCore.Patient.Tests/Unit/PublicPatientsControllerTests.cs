using System.Reflection;
using MediCore.Patient.Api.Controllers;
using MediCore.Patient.Application.DTOs;
using MediCore.Patient.Application.Services;
using MediCore.Patient.Application.Validators;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Logging.Abstractions;

namespace MediCore.Patient.Tests.Unit;

public sealed class PublicPatientsControllerTests
{
    private static readonly Guid PatientId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly DateOnly DateOfBirth = new(1995, 4, 2);
    private static readonly DateTime Now = new(2026, 9, 23, 8, 0, 0, DateTimeKind.Utc);

    // ── Identify ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Identifying_successfully_hands_back_a_booking_token()
    {
        var controller = CreateController(new FakeIdentificationService(Identity()));

        var result = await controller.Identify(
            new IdentifyPatientRequest("PAT-000123", DateOfBirth), CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var identity = Assert.IsType<BookingIdentityResponse>(ok.Value);
        Assert.Equal(PatientId, identity.PatientId);
        Assert.Equal("booking-token", identity.BookingToken);
    }

    [Fact]
    public async Task A_failed_identification_says_nothing_about_which_half_was_wrong()
    {
        var controller = CreateController(new FakeIdentificationService(identity: null));

        var result = await controller.Identify(
            new IdentifyPatientRequest("PAT-999999", DateOfBirth), CancellationToken.None);

        var notFound = Assert.IsType<NotFoundObjectResult>(result);
        var problem = Assert.IsType<ProblemDetails>(notFound.Value);
        Assert.Equal(
            "We could not find a patient with that number and date of birth.",
            problem.Title);
        // Nothing that would confirm the number exists.
        Assert.DoesNotContain("PAT-999999", problem.Title);
        Assert.Null(problem.Detail);
    }

    [Fact]
    public async Task An_empty_patient_number_is_a_validation_failure_not_a_lookup()
    {
        var identification = new FakeIdentificationService(identity: null);
        var controller = CreateController(identification);

        var result = await controller.Identify(
            new IdentifyPatientRequest("", DateOfBirth), CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        var problem = Assert.IsType<ValidationProblemDetails>(badRequest.Value);
        Assert.Contains("patientNumber", problem.Errors.Keys);
        // The lookup never ran, so a malformed request cannot be used to probe for patients.
        Assert.Equal(0, identification.IdentifyCount);
    }

    // ── Public registration ───────────────────────────────────────────────────

    [Fact]
    public async Task Registering_publicly_returns_the_new_patient_number_and_a_token()
    {
        var controller = CreateController(
            new FakeIdentificationService(Identity()),
            new FakeRegistrationService(new PatientRegisteredResult(Registered())));

        var result = await controller.PublicRegister(ValidRequest(), CancellationToken.None);

        var created = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status201Created, created.StatusCode);
        var identity = Assert.IsType<BookingIdentityResponse>(created.Value);
        Assert.Equal("PAT-000456", identity.PatientNumber);
        Assert.Equal("booking-token", identity.BookingToken);
    }

    [Fact]
    public async Task Public_registration_is_attributed_to_the_booking_page_not_to_a_user()
    {
        var registration = new FakeRegistrationService(new PatientRegisteredResult(Registered()));
        var controller = CreateController(new FakeIdentificationService(Identity()), registration);

        await controller.PublicRegister(ValidRequest(), CancellationToken.None);

        Assert.Equal("public-booking", registration.LastCreatedBy);
    }

    [Fact]
    public async Task A_taken_nic_reveals_nothing_about_the_patient_who_holds_it()
    {
        // The front desk's DuplicatePatientResponse carries the existing patient's number, name,
        // email and archived flag. On an anonymous endpoint that is an oracle keyed by a stolen
        // NIC: post it, learn a name and a patient number, then use them to identify.
        var existing = new ExistingPatientSummary(
            Guid.NewGuid(), "PAT-000123", "Nimal Perera", "nimal@example.com", false);
        var controller = CreateController(
            new FakeIdentificationService(Identity()),
            new FakeRegistrationService(new DuplicatePatientResult(existing)));

        var result = await controller.PublicRegister(ValidRequest(), CancellationToken.None);

        var conflict = Assert.IsType<ConflictObjectResult>(result);
        var problem = Assert.IsType<ProblemDetails>(conflict.Value);
        var body = problem.Title + problem.Detail;
        Assert.DoesNotContain("PAT-000123", body);
        Assert.DoesNotContain("Nimal", body);
        Assert.DoesNotContain("nimal@example.com", body);
        Assert.Contains("patient number and date of birth", body);
    }

    // ── The shape of the anonymous surface ────────────────────────────────────

    [Fact]
    public void The_anonymous_endpoints_live_on_their_own_controller()
    {
        Assert.NotNull(typeof(PublicPatientsController).GetCustomAttribute<AllowAnonymousAttribute>());
    }

    [Fact]
    public void The_anonymous_controller_is_rate_limited_more_tightly_than_the_rest()
    {
        var attribute = typeof(PublicPatientsController)
            .GetCustomAttribute<EnableRateLimitingAttribute>();

        Assert.NotNull(attribute);
        Assert.Equal(PublicRateLimitPolicies.PublicBooking, attribute.PolicyName);
    }

    [Fact]
    public void The_authorized_patient_controller_gained_no_anonymous_methods()
    {
        // PatientsController is [Authorize] with a policy on every method. Dropping the public
        // endpoints in there would have left it quietly half-anonymous, and no other test in this
        // suite checks per-method anonymity — this one does.
        Assert.NotNull(typeof(PatientsController).GetCustomAttribute<AuthorizeAttribute>());
        Assert.Empty(typeof(PatientsController)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(method => method.GetCustomAttribute<AllowAnonymousAttribute>() is not null));
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static PublicPatientsController CreateController(
        FakeIdentificationService identification,
        FakeRegistrationService? registration = null) =>
        new(
            new IdentifyPatientRequestValidator(),
            new CreatePatientRequestValidator(new FixedTimeProvider(Now)),
            identification,
            registration ?? new FakeRegistrationService(new PatientRegisteredResult(Registered())),
            NullLogger<PublicPatientsController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };

    private static BookingIdentityResponse Identity() => new(
        PatientId,
        "PAT-000456",
        "Kamala Silva",
        "booking-token",
        new DateTime(2026, 9, 23, 8, 20, 0, DateTimeKind.Utc));

    private static PatientRegistrationResponse Registered() => new(
        PatientId,
        "PAT-000456",
        "199504201234",
        "Kamala",
        "Silva",
        "Kamala Silva",
        DateOfBirth,
        "Female",
        "kamala@example.com",
        "0771234567",
        "1 Galle Road",
        null,
        "Colombo",
        null,
        null,
        new DateTime(2026, 9, 23, 8, 0, 0, DateTimeKind.Utc));

    private static CreatePatientRequest ValidRequest() => new(
        "199504201234",
        "Kamala",
        "Silva",
        DateOfBirth,
        "Female",
        "kamala@example.com",
        "0771234567",
        "1 Galle Road",
        null,
        "Colombo",
        null,
        null);

    private sealed class FakeIdentificationService : IPatientIdentificationService
    {
        private readonly BookingIdentityResponse? _identity;

        public FakeIdentificationService(BookingIdentityResponse? identity)
        {
            _identity = identity;
        }

        public int IdentifyCount { get; private set; }

        public Task<BookingIdentityResponse?> IdentifyAsync(
            string patientNumber, DateOnly dateOfBirth, CancellationToken cancellationToken = default)
        {
            IdentifyCount++;
            return Task.FromResult(_identity);
        }

        public BookingIdentityResponse IssueFor(Guid patientId, string patientNumber, string fullName) =>
            _identity ?? throw new NotSupportedException("This fixture issues no token.");
    }

    private sealed class FakeRegistrationService : IPatientRegistrationService
    {
        private readonly PatientRegistrationResult _result;

        public FakeRegistrationService(PatientRegistrationResult result)
        {
            _result = result;
        }

        public string? LastCreatedBy { get; private set; }

        public Task<PatientRegistrationResult> RegisterAsync(
            CreatePatientRequest request,
            string correlationId,
            string createdBy,
            CancellationToken cancellationToken = default)
        {
            LastCreatedBy = createdBy;
            return Task.FromResult(_result);
        }
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _now;

        public FixedTimeProvider(DateTime now)
        {
            _now = now;
        }

        public override DateTimeOffset GetUtcNow() => _now;
    }
}
