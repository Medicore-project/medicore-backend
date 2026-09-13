using MediCore.Patient.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using PatientEntity = MediCore.Patient.Application.Entities.Patient;

namespace MediCore.Patient.Tests.Unit;

public sealed class PatientDbContextModelTests
{
    [Fact]
    public void Patient_entity_has_a_global_soft_delete_query_filter()
    {
        var options = new DbContextOptionsBuilder<PatientDbContext>()
            .UseNpgsql("Host=localhost;Database=unused;Username=unused;Password=unused")
            .Options;
        using var context = new PatientDbContext(options);

        var entityType = context.Model.FindEntityType(typeof(PatientEntity));

        Assert.NotNull(entityType);
        Assert.NotNull(entityType.GetQueryFilter());
    }
}
