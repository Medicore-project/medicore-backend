using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using MediCore.Patient.Application.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace MediCore.Patient.Infrastructure.Auth;

/// <inheritdoc cref="IBookingTokenGenerator"/>
public sealed class BookingTokenGenerator : IBookingTokenGenerator
{
    /// <summary>
    /// Long enough to fill in a demographics form, choose a slot, and try again after losing a
    /// slot to someone else; short enough that a shared clinic browser forgets it quickly.
    /// </summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(20);

    /// <summary>Names the one patient this token may book for.</summary>
    public const string PatientIdClaim = "patientId";

    /// <summary>
    /// The patient's number and name, recorded on the appointment so staff can see who booked.
    /// Custom claim types rather than the registered <c>name</c>, which a JWT handler's inbound
    /// claim mapping may rename on the way in.
    /// </summary>
    public const string PatientNumberClaim = "patientNumber";

    /// <inheritdoc cref="PatientNumberClaim"/>
    public const string PatientNameClaim = "patientName";

    /// <summary>
    /// Marks the token's purpose, so a reviewer reading a decoded payload can see at a glance that
    /// this is not an access token.
    /// </summary>
    public const string TokenUseClaim = "token_use";

    public const string BookingTokenUse = "booking";

    private readonly IConfiguration _configuration;
    private readonly TimeProvider _timeProvider;

    public BookingTokenGenerator(IConfiguration configuration, TimeProvider timeProvider)
    {
        _configuration = configuration;
        _timeProvider = timeProvider;
    }

    public (string Token, DateTime ExpiresAtUtc) Generate(
        Guid patientId,
        string patientNumber,
        string fullName)
    {
        var signingKey = _configuration["Jwt:Key"]
            ?? throw new InvalidOperationException("Jwt:Key configuration is missing.");

        var expiresAtUtc = _timeProvider.GetUtcNow().UtcDateTime.Add(Lifetime);

        // No role, no email, no staffId — see IBookingTokenGenerator for why the absence of a role
        // claim is the security property here, not an oversight. The number and name are
        // descriptive only: no policy anywhere reads them, and the patient already holds both.
        var claims = new List<Claim>
        {
            // sub, so the appointment service's CurrentActor() stamps the patient's id on the
            // audit columns rather than falling through to "system".
            new(JwtRegisteredClaimNames.Sub, patientId.ToString()),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new(PatientIdClaim, patientId.ToString()),
            new(PatientNumberClaim, patientNumber),
            new(PatientNameClaim, fullName),
            new(TokenUseClaim, BookingTokenUse)
        };

        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)),
            SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _configuration["Jwt:Issuer"],
            audience: _configuration["Jwt:Audience"],
            claims: claims,
            expires: expiresAtUtc,
            signingCredentials: credentials);

        return (new JwtSecurityTokenHandler().WriteToken(token), expiresAtUtc);
    }
}
