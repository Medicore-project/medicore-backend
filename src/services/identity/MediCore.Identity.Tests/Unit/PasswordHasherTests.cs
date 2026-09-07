using System.Text.RegularExpressions;
using MediCore.Identity.Infrastructure.Security;

namespace MediCore.Identity.Tests.Unit;

public sealed class PasswordHasherTests
{
    private readonly PasswordHasher _hasher = new();

    [Fact]
    public void HashPassword_uses_bcrypt_work_factor_12()
    {
        var hash = _hasher.HashPassword("Admin@12345");

        // BCrypt hash format: $<algorithm>$<cost>$<22-char-salt><31-char-hash>
        var match = Regex.Match(hash, @"^\$2[aby]?\$(\d{2})\$");
        Assert.True(match.Success, $"Hash '{hash}' is not a recognizable BCrypt hash.");
        Assert.Equal("12", match.Groups[1].Value);
    }

    [Fact]
    public void VerifyPassword_returns_true_for_the_correct_password()
    {
        var hash = _hasher.HashPassword("Correct-Horse-Battery-Staple1");

        Assert.True(_hasher.VerifyPassword("Correct-Horse-Battery-Staple1", hash));
    }

    [Fact]
    public void VerifyPassword_returns_false_for_the_wrong_password()
    {
        var hash = _hasher.HashPassword("Correct-Horse-Battery-Staple1");

        Assert.False(_hasher.VerifyPassword("wrong-password", hash));
    }

    [Fact]
    public void HashPassword_never_stores_the_plaintext_password()
    {
        const string password = "Admin@12345";

        var hash = _hasher.HashPassword(password);

        Assert.DoesNotContain(password, hash);
    }

    [Fact]
    public void HashPassword_produces_a_different_hash_each_time_due_to_salting()
    {
        const string password = "Admin@12345";

        var hash1 = _hasher.HashPassword(password);
        var hash2 = _hasher.HashPassword(password);

        Assert.NotEqual(hash1, hash2);
        // ...but both still verify correctly against the same plaintext.
        Assert.True(_hasher.VerifyPassword(password, hash1));
        Assert.True(_hasher.VerifyPassword(password, hash2));
    }
}
