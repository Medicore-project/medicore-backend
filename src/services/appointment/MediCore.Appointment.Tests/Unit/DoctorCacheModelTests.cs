using MediCore.Appointment.Application.Entities;
using MediCore.Appointment.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace MediCore.Appointment.Tests.Unit;

public sealed class DoctorCacheModelTests
{
    [Fact]
    public void Doctor_cache_maps_to_cached_doctors_with_a_soft_delete_filter()
    {
        using var context = CreateContext();
        var entityType = context.Model.FindEntityType(typeof(DoctorCache));

        Assert.NotNull(entityType);
        Assert.Equal("cached_doctors", entityType.GetTableName());
        Assert.Equal(AppointmentDbContext.SchemaName, entityType.GetSchema());
        Assert.NotNull(entityType.GetQueryFilter());
    }

    [Fact]
    public void Doctor_id_is_unique_among_live_rows_only()
    {
        using var context = CreateContext();
        var entityType = context.Model.FindEntityType(typeof(DoctorCache))!;

        var index = Assert.Single(entityType.GetIndexes(), i =>
            i.Properties.Select(p => p.Name).SequenceEqual([nameof(DoctorCache.DoctorId)]));

        Assert.True(index.IsUnique);
        Assert.Equal("ux_cached_doctors_doctor_id", index.GetDatabaseName());
        Assert.Equal("\"IsDeleted\" = false", index.GetFilter());
    }

    [Fact]
    public void Bookable_listing_is_indexed_on_active_then_specialization()
    {
        using var context = CreateContext();
        var entityType = context.Model.FindEntityType(typeof(DoctorCache))!;

        var index = Assert.Single(entityType.GetIndexes(), i =>
            i.GetDatabaseName() == "ix_cached_doctors_active_specialization");

        Assert.Equal(
            [nameof(DoctorCache.IsActive), nameof(DoctorCache.Specialization)],
            index.Properties.Select(p => p.Name));
        Assert.False(index.IsUnique);
    }

    [Fact]
    public void Is_active_is_not_database_generated_so_false_is_always_inserted()
    {
        using var context = CreateContext();
        var property = context.Model.FindEntityType(typeof(DoctorCache))!
            .FindProperty(nameof(DoctorCache.IsActive))!;

        Assert.Equal(ValueGenerated.Never, property.ValueGenerated);
        // GetDefaultValue() reports the CLR default even when nothing is configured, so look for the
        // annotation HasDefaultValue would have written.
        Assert.Null(property.FindAnnotation(RelationalAnnotationNames.DefaultValue));
    }

    [Fact]
    public void Processed_message_is_keyed_by_message_id_under_the_constraint_name_the_unit_of_work_matches()
    {
        using var context = CreateContext();
        var entityType = context.Model.FindEntityType(typeof(ProcessedMessage));

        Assert.NotNull(entityType);
        Assert.Equal("processed_messages", entityType.GetTableName());

        var key = entityType.FindPrimaryKey()!;
        Assert.Equal([nameof(ProcessedMessage.MessageId)], key.Properties.Select(p => p.Name));
        Assert.Equal("pk_processed_messages", key.GetName());
    }

    private static AppointmentDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<AppointmentDbContext>()
            .UseNpgsql("Host=localhost;Database=unused;Username=unused;Password=unused")
            .Options);
}
