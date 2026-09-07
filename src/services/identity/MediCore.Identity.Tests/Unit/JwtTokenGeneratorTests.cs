using System.IdentityModel.Tokens.Jwt;
using MediCore.Identity.Application.Entities;
using MediCore.Identity.Infrastructure.Auth;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Primitives;

namespace MediCore.Identity.Tests.Unit;

/// <summary>
/// Minimal IConfiguration test double so these tests don't need a real appsettings.json
/// or an extra Microsoft.Extensions.Configuration.Memory package reference.
/// </summary>
internal sealed class InMemoryTestConfiguration : IConfiguration
{
    private readonly Dictionary<string, string?> _values;

    public InMemoryTestConfiguration(Dictionary<string, string?> values) => _values = values;

    public string? this[string key]
    {
        get => _values.TryGetValue(key, out var value) ? value : null;
        set => _values[key] = value;
    }

    public IEnumerable<IConfigurationSection> GetChildren() => Enumerable.Empty<IConfigurationSection>();
    public IChangeToken GetReloadToken() => NullChangeToken.Instance;
    public IConfigurationSection GetSection(string key) => throw new NotSupportedException();

    private sealed class NullChangeToken : IChangeToken
    {
        public static readonly NullChangeToken Instance = new();
        public bool HasChanged => false;
        public bool ActiveChangeCallbacks => false;
        public IDisposable RegisterChangeCallback(Action<object?> callback, object? state) => new NoopDisposable();
        private sealed class NoopDisposable : IDisposable { public void Dispose() { } }
    }
}

public sealed class JwtTokenGeneratorTests
{
    private static JwtTokenGenerator CreateGenerator() => new(new InMemoryTestConfiguration(new Dictionary<string, string?>
    {
        ["Jwt:Key"] = "Test-Only-Signing-Key-That-Is-At-Least-32-Characters-Long!",
        ["Jwt:Issuer"] = "medicore-identity-tests",
        ["Jwt:Audience"] = "medicore-clients-tests",
    }));

    private static User SampleAdmin() => new()
    {
        Id = Guid.NewGuid(),
        Email = "admin@medicore.local",
        Role = "Admin",
        PasswordHash = "irrelevant-for-this-test",
    };

    [Fact]
    public void GenerateAccessToken_includes_sub_email_role_and_jti_claims()
    {
        var generator = CreateGenerator();
        var user = SampleAdmin();

        var token = generator.GenerateAccessToken(user);
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);

        Assert.Equal(user.Id.ToString(), jwt.Claims.Single(c => c.Type == JwtRegisteredClaimNames.Sub).Value);
        Assert.Equal(user.Email, jwt.Claims.Single(c => c.Type == JwtRegisteredClaimNames.Email).Value);
        Assert.Equal(user.Role, jwt.Claims.Single(c => c.Type == System.Security.Claims.ClaimTypes.Role).Value);
        Assert.True(Guid.TryParse(jwt.Claims.Single(c => c.Type == JwtRegisteredClaimNames.Jti).Value, out _));
    }

    [Fact]
    public void GenerateAccessToken_sets_issuer_and_audience()
    {
        var generator = CreateGenerator();

        var token = generator.GenerateAccessToken(SampleAdmin());
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);

        Assert.Equal("medicore-identity-tests", jwt.Issuer);
        Assert.Equal("medicore-clients-tests", jwt.Audiences.Single());
    }

    [Fact]
    public void GenerateAccessToken_expires_in_approximately_15_minutes()
    {
        var generator = CreateGenerator();

        var token = generator.GenerateAccessToken(SampleAdmin());
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);

        var expiresIn = jwt.ValidTo - DateTime.UtcNow;
        Assert.InRange(expiresIn.TotalMinutes, 14, 16);
    }

    [Fact]
    public void GenerateAccessToken_changes_the_role_claim_when_the_users_role_changes()
    {
        var generator = CreateGenerator();
        var user = SampleAdmin();

        var adminToken = new JwtSecurityTokenHandler().ReadJwtToken(generator.GenerateAccessToken(user));

        user.Role = "Doctor"; // simulates POST /staff/{id}/roles updating the role, then a fresh login/refresh
        var doctorToken = new JwtSecurityTokenHandler().ReadJwtToken(generator.GenerateAccessToken(user));

        Assert.Equal("Admin", adminToken.Claims.Single(c => c.Type == System.Security.Claims.ClaimTypes.Role).Value);
        Assert.Equal("Doctor", doctorToken.Claims.Single(c => c.Type == System.Security.Claims.ClaimTypes.Role).Value);
    }

    [Fact]
    public void GenerateRefreshToken_returns_a_url_safe_high_entropy_token_each_call()
    {
        var generator = CreateGenerator();

        var token1 = generator.GenerateRefreshToken();
        var token2 = generator.GenerateRefreshToken();

        Assert.NotEqual(token1, token2);
        Assert.True(Convert.FromBase64String(token1).Length >= 32);
    }
}
