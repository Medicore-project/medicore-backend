using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace MediCore.Appointment.Tests.Integration;

/// <summary>
/// The real appointment API, in process, against the local Docker Postgres
/// (<c>docker compose up -d postgres</c>) — and with neither the Identity service nor Kafka.
/// </summary>
/// <remarks>
/// <para>
/// Nothing in this host can reach Identity: the service has no HTTP client for it, and tokens are
/// minted here with the same signing settings the host validates against, exactly as a token issued
/// by Identity earlier would be checked. That is the situation SCRUM-33 AC3 describes — Identity
/// down, booking still working from cached data.
/// </para>
/// <para>
/// Kafka is switched off by blanking <c>Kafka:BootstrapServers</c>, so the staff-events consumer is
/// not registered. Doctor data is put in the cache through the same handler the consumer calls.
/// </para>
/// </remarks>
public sealed class AppointmentApiFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("Kafka:BootstrapServers", string.Empty);
    }

    /// <summary>A client carrying a bearer token for <paramref name="role"/>.</summary>
    public HttpClient CreateClientAs(string role, Guid? staffId = null)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", CreateToken(role, staffId));
        return client;
    }

    /// <summary>
    /// A token shaped like the ones Identity issues (see Identity's <c>JwtTokenGenerator</c>),
    /// signed with the settings this host loaded.
    /// </summary>
    private string CreateToken(string role, Guid? staffId)
    {
        var configuration = Services.GetRequiredService<IConfiguration>();
        var key = configuration["Jwt:Key"]
            ?? throw new InvalidOperationException("Jwt:Key is not configured for the test host.");

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, Guid.NewGuid().ToString()),
            new(JwtRegisteredClaimNames.Email, $"{role.ToLowerInvariant()}@medicore.test"),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new(ClaimTypes.Role, role)
        };
        if (staffId is not null)
        {
            claims.Add(new Claim("staffId", staffId.Value.ToString()));
        }

        var token = new JwtSecurityToken(
            issuer: configuration["Jwt:Issuer"] ?? "medicore-identity",
            audience: configuration["Jwt:Audience"] ?? "medicore-clients",
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(10),
            signingCredentials: new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)),
                SecurityAlgorithms.HmacSha256));

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
