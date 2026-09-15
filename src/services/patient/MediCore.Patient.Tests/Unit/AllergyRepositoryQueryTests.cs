using MediCore.Patient.Infrastructure.Persistence;
using MediCore.Patient.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace MediCore.Patient.Tests.Unit;

public sealed class AllergyRepositoryQueryTests
{
    [Fact]
    public void Conflict_query_translates_both_substring_directions_to_postgresql()
    {
        var options = new DbContextOptionsBuilder<PatientDbContext>()
            .UseNpgsql("Host=localhost;Database=unused;Username=unused;Password=unused")
            .Options;
        using var dbContext = new PatientDbContext(options);
        var repository = new AllergyRepository(dbContext);

        var sql = repository.BuildConflictQuery(Guid.NewGuid(), "Penicillin").ToQueryString();

        Assert.Equal(2, CountOccurrences(sql, "ILIKE"));
        Assert.Contains("a.\"Allergen\" || '%'", sql);
        Assert.DoesNotContain("string.Format", sql, StringComparison.OrdinalIgnoreCase);
    }

    private static int CountOccurrences(string value, string expected) =>
        value.Split(expected, StringSplitOptions.None).Length - 1;
}
