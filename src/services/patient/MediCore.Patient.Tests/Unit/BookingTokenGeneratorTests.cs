using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using MediCore.Patient.Infrastructure.Auth;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace MediCore.Patient.Tests.Unit;

public sealed class BookingTokenGeneratorTests
{
    private const string SigningKey = "a-development-signing-key-long-enough-for-hmac-sha256";
    private const string Issuer = "medicore-identity";
    private const string Audience = "medicore-clients";

    private static readonly DateTime Now = new(2026, 9, 23, 8, 0, 0, DateTimeKind.Utc);
    private static readonly Guid PatientId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private const string PatientNumber = "PAT-000123";
    private const string FullName = "Nimal Perera";

    [Fact]
    public void The_token_names_the_one_patient_it_can_book_for()
    {
        var token = Read(Generate().Token);

        Assert.Equal(
            PatientId.ToString(),
            token.Claims.Single(c => c.Type == BookingTokenGenerator.PatientIdClaim).Value);
        Assert.Equal(PatientId.ToString(), token.Claims.Single(c => c.Type == "sub").Value);
        Assert.Equal(
            BookingTokenGenerator.BookingTokenUse,
            token.Claims.Single(c => c.Type == BookingTokenGenerator.TokenUseClaim).Value);
        Assert.Single(token.Claims, c => c.Type == "jti");
    }

    [Fact]
    public void The_token_carries_the_number_and_name_the_appointment_is_recorded_under()
    {
        var token = Read(Generate().Token);

        Assert.Equal(
            PatientNumber,
            token.Claims.Single(c => c.Type == BookingTokenGenerator.PatientNumberClaim).Value);
        Assert.Equal(
            FullName,
            token.Claims.Single(c => c.Type == BookingTokenGenerator.PatientNameClaim).Value);
    }

    [Fact]
    public void The_token_carries_no_role_which_is_what_keeps_it_harmless_everywhere_else()
    {
        // The security property, asserted rather than assumed. Every policy in both services is
        // role-based except BookingCreator, so a role-less principal fails all of them. Adding
        // role "Patient" for tidiness would make a future RequireRole("Patient") silently widen
        // every token already issued.
        var token = Read(Generate().Token);

        Assert.DoesNotContain(token.Claims, c => c.Type == ClaimTypes.Role);
        Assert.DoesNotContain(token.Claims, c => c.Type == "role");
        Assert.DoesNotContain(token.Claims, c => c.Type == "staffId");
        Assert.DoesNotContain(token.Claims, c => c.Type == "email");
        Assert.DoesNotContain(token.Claims, c => c.Type == ClaimTypes.Email);
    }

    [Fact]
    public void The_token_lasts_twenty_minutes()
    {
        var (_, expiresAtUtc) = Generate();

        Assert.Equal(Now.AddMinutes(20), expiresAtUtc);
        Assert.Equal(TimeSpan.FromMinutes(20), BookingTokenGenerator.Lifetime);
    }

    [Fact]
    public void The_token_is_issued_for_the_audience_the_other_services_validate()
    {
        var token = Read(Generate().Token);

        Assert.Equal(Issuer, token.Issuer);
        Assert.Contains(Audience, token.Audiences);
    }

    [Fact]
    public void The_appointment_service_accepts_it_on_the_shared_signing_key()
    {
        // The load-bearing assumption of the whole public booking flow: the appointment service
        // validates this token with the JWT bearer handler it already has, using the symmetric key
        // every service is given. These are the parameters its Program.cs builds, so a mismatch
        // shows up here rather than as a 401 nobody can explain.
        var parameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = Issuer,
            ValidAudience = Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningKey)),
            LifetimeValidator = (_, expires, _, _) => expires > Now
        };

        var principal = new JwtSecurityTokenHandler()
            .ValidateToken(Generate().Token, parameters, out _);

        Assert.Equal(
            PatientId.ToString(),
            principal.FindFirst(BookingTokenGenerator.PatientIdClaim)!.Value);
    }

    [Fact]
    public void Two_tokens_for_the_same_patient_are_distinguishable()
    {
        var generator = CreateGenerator();

        Assert.NotEqual(
            Read(generator.Generate(PatientId, PatientNumber, FullName).Token).Claims.Single(c => c.Type == "jti").Value,
            Read(generator.Generate(PatientId, PatientNumber, FullName).Token).Claims.Single(c => c.Type == "jti").Value);
    }

    [Fact]
    public void A_missing_signing_key_fails_loudly_rather_than_minting_an_unusable_token()
    {
        var generator = new BookingTokenGenerator(
            new ConfigurationBuilder().AddInMemoryCollection([]).Build(),
            new FixedTimeProvider(Now));

        Assert.Throws<InvalidOperationException>(() => generator.Generate(PatientId, PatientNumber, FullName));
    }

    private static (string Token, DateTime ExpiresAtUtc) Generate() =>
        CreateGenerator().Generate(PatientId, PatientNumber, FullName);

    private static BookingTokenGenerator CreateGenerator() =>
        new(
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Jwt:Key"] = SigningKey,
                    ["Jwt:Issuer"] = Issuer,
                    ["Jwt:Audience"] = Audience
                })
                .Build(),
            new FixedTimeProvider(Now));

    private static JwtSecurityToken Read(string token) =>
        new JwtSecurityTokenHandler().ReadJwtToken(token);

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
