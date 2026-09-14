using ConditionEntity = MediCore.Patient.Application.Entities.Condition;
using MedicalRecordEntity = MediCore.Patient.Application.Entities.MedicalRecord;

namespace MediCore.Patient.Tests.Unit;

public sealed class MedicalRecordEntityTests
{
    [Fact]
    public void New_medical_record_uses_safe_versioning_defaults()
    {
        var record = new MedicalRecordEntity();

        Assert.NotEqual(Guid.Empty, record.Id);
        Assert.NotEqual(Guid.Empty, record.RecordId);
        Assert.NotEqual(record.Id, record.RecordId);
        Assert.Equal(1, record.Version);
        Assert.True(record.IsCurrent);
        Assert.False(record.IsDeleted);
        Assert.Empty(record.Conditions);
        Assert.Null(record.PreviousVersion);
        Assert.Null(record.PreviousVersionId);
    }

    [Fact]
    public void Medical_record_represents_an_authored_visit_version_with_condition_snapshot()
    {
        var patientId = Guid.NewGuid();
        var visitReference = Guid.NewGuid();
        var authoredAt = new DateTime(2026, 9, 13, 9, 30, 0, DateTimeKind.Utc);
        var createdAt = authoredAt.AddMinutes(1);
        var updatedAt = authoredAt.AddMinutes(2);
        var previous = new MedicalRecordEntity { Version = 1, IsCurrent = false };
        var condition = new ConditionEntity
        {
            MedicalRecordId = Guid.NewGuid(),
            Name = "Hypertension",
            Code = "I10",
            ClinicalStatus = "Active",
            Notes = "Monitor blood pressure",
            IsDeleted = false,
            CreatedAt = createdAt,
            UpdatedAt = updatedAt,
            CreatedBy = "doctor-1",
            UpdatedBy = "nurse-1"
        };
        var record = new MedicalRecordEntity
        {
            RecordId = previous.RecordId,
            PatientId = patientId,
            VisitReference = visitReference,
            ClinicalNotes = "Follow-up consultation",
            AuthorClinicianId = "doctor-1",
            AuthorClinicianEmail = "doctor@medicore.test",
            AuthorClinicianRole = "Doctor",
            AuthoredAtUtc = authoredAt,
            Version = 2,
            PreviousVersionId = previous.Id,
            PreviousVersion = previous,
            IsCurrent = true,
            IsDeleted = false,
            CreatedAt = createdAt,
            UpdatedAt = updatedAt,
            CreatedBy = "doctor-1",
            UpdatedBy = "nurse-1",
            Conditions = [condition]
        };
        condition.MedicalRecordId = record.Id;
        condition.MedicalRecord = record;

        Assert.Equal(patientId, record.PatientId);
        Assert.Equal(visitReference, record.VisitReference);
        Assert.Equal("Follow-up consultation", record.ClinicalNotes);
        Assert.Equal("doctor-1", record.AuthorClinicianId);
        Assert.Equal("doctor@medicore.test", record.AuthorClinicianEmail);
        Assert.Equal("Doctor", record.AuthorClinicianRole);
        Assert.Equal(authoredAt, record.AuthoredAtUtc);
        Assert.Equal(2, record.Version);
        Assert.Equal(previous.Id, record.PreviousVersionId);
        Assert.Same(previous, record.PreviousVersion);
        Assert.True(record.IsCurrent);
        Assert.False(record.IsDeleted);
        Assert.Equal(createdAt, record.CreatedAt);
        Assert.Equal(updatedAt, record.UpdatedAt);
        Assert.Equal("doctor-1", record.CreatedBy);
        Assert.Equal("nurse-1", record.UpdatedBy);

        var storedCondition = Assert.Single(record.Conditions);
        Assert.NotEqual(Guid.Empty, storedCondition.Id);
        Assert.Equal(record.Id, storedCondition.MedicalRecordId);
        Assert.Equal("Hypertension", storedCondition.Name);
        Assert.Equal("I10", storedCondition.Code);
        Assert.Equal("Active", storedCondition.ClinicalStatus);
        Assert.Equal("Monitor blood pressure", storedCondition.Notes);
        Assert.False(storedCondition.IsDeleted);
        Assert.Equal(createdAt, storedCondition.CreatedAt);
        Assert.Equal(updatedAt, storedCondition.UpdatedAt);
        Assert.Equal("doctor-1", storedCondition.CreatedBy);
        Assert.Equal("nurse-1", storedCondition.UpdatedBy);
        Assert.Same(record, storedCondition.MedicalRecord);
    }
}
