using MediCore.Patient.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using ConditionEntity = MediCore.Patient.Application.Entities.Condition;
using MedicalRecordEntity = MediCore.Patient.Application.Entities.MedicalRecord;
using PatientEntity = MediCore.Patient.Application.Entities.Patient;
using ProcessedMessageEntity = MediCore.Patient.Application.Entities.ProcessedMessage;

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

    [Fact]
    public void Medical_record_and_condition_have_global_soft_delete_filters()
    {
        using var context = CreateContext();

        var medicalRecord = context.Model.FindEntityType(typeof(MedicalRecordEntity));
        var condition = context.Model.FindEntityType(typeof(ConditionEntity));

        Assert.NotNull(medicalRecord?.GetQueryFilter());
        Assert.NotNull(condition?.GetQueryFilter());
    }

    [Fact]
    public void Medical_record_model_enforces_version_history_invariants()
    {
        using var context = CreateContext();
        var entityType = context.Model.FindEntityType(typeof(MedicalRecordEntity));

        Assert.NotNull(entityType);

        var versionIndex = Assert.Single(entityType.GetIndexes(), index =>
            index.Properties.Select(property => property.Name)
                .SequenceEqual([nameof(MedicalRecordEntity.RecordId), nameof(MedicalRecordEntity.Version)]));
        Assert.True(versionIndex.IsUnique);

        var currentIndex = Assert.Single(entityType.GetIndexes(), index =>
            index.Properties.Select(property => property.Name)
                .SequenceEqual([nameof(MedicalRecordEntity.RecordId)]));
        Assert.True(currentIndex.IsUnique);
        Assert.Contains(nameof(MedicalRecordEntity.IsCurrent), currentIndex.GetFilter());

        var previousVersionForeignKey = Assert.Single(entityType.GetForeignKeys(), foreignKey =>
            foreignKey.Properties.Single().Name == nameof(MedicalRecordEntity.PreviousVersionId));
        Assert.Equal(DeleteBehavior.Restrict, previousVersionForeignKey.DeleteBehavior);
    }

    [Fact]
    public void Medical_record_relations_use_restrictive_delete_behavior()
    {
        using var context = CreateContext();
        var recordType = context.Model.FindEntityType(typeof(MedicalRecordEntity));
        var conditionType = context.Model.FindEntityType(typeof(ConditionEntity));

        Assert.NotNull(recordType);
        Assert.NotNull(conditionType);

        var patientForeignKey = Assert.Single(recordType.GetForeignKeys(), foreignKey =>
            foreignKey.PrincipalEntityType.ClrType == typeof(PatientEntity));
        var conditionForeignKey = Assert.Single(conditionType.GetForeignKeys(), foreignKey =>
            foreignKey.PrincipalEntityType.ClrType == typeof(MedicalRecordEntity));

        Assert.Equal(DeleteBehavior.Restrict, patientForeignKey.DeleteBehavior);
        Assert.Equal(DeleteBehavior.Restrict, conditionForeignKey.DeleteBehavior);
    }

    [Fact]
    public void Processed_message_id_is_the_database_primary_key()
    {
        using var context = CreateContext();
        var entityType = context.Model.FindEntityType(typeof(ProcessedMessageEntity));

        Assert.NotNull(entityType);
        var primaryKey = Assert.IsAssignableFrom<IKey>(entityType.FindPrimaryKey());
        Assert.Equal("pk_processed_messages", primaryKey.GetName());
        Assert.Equal(nameof(ProcessedMessageEntity.MessageId), Assert.Single(primaryKey.Properties).Name);
    }

    private static PatientDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<PatientDbContext>()
            .UseNpgsql("Host=localhost;Database=unused;Username=unused;Password=unused")
            .Options;

        return new PatientDbContext(options);
    }
}
