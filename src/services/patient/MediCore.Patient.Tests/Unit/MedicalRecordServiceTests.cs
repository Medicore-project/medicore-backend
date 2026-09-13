using MediCore.Patient.Application.DTOs;
using MediCore.Patient.Application.Entities;
using MediCore.Patient.Application.Exceptions;
using MediCore.Patient.Application.Interfaces;
using MediCore.Patient.Application.Services;
using MedicalRecordEntity = MediCore.Patient.Application.Entities.MedicalRecord;
using PatientEntity = MediCore.Patient.Application.Entities.Patient;

namespace MediCore.Patient.Tests.Unit;

public sealed class MedicalRecordServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 14, 10, 15, 0, TimeSpan.Zero);
    private static readonly PatientAccessContext Access = new(
        "clinician-26", "Doctor", "corr-26", "127.0.0.1", "DOCTOR@MEDICORE.LK");

    [Fact]
    public async Task Create_stores_server_derived_author_time_visit_and_conditions()
    {
        var fixture = new Fixture(ExistingPatient());
        var visitReference = Guid.NewGuid();

        var result = await fixture.Service.CreateAsync(
            fixture.Patient!.Id,
            new CreateMedicalRecordRequest(
                visitReference,
                "  Initial consultation notes.  ",
                [new(" Hypertension ", " I10 ", "active", " Monitor BP ")]),
            Access);

        var created = Assert.IsType<MedicalRecordCreatedResult>(result).Record;
        var entity = Assert.Single(fixture.Records.Added);
        Assert.Equal(visitReference, entity.VisitReference);
        Assert.Equal("Initial consultation notes.", entity.ClinicalNotes);
        Assert.Equal("clinician-26", entity.AuthorClinicianId);
        Assert.Equal("doctor@medicore.lk", entity.AuthorClinicianEmail);
        Assert.Equal("Doctor", entity.AuthorClinicianRole);
        Assert.Equal(Now.UtcDateTime, entity.AuthoredAtUtc);
        Assert.Equal(1, entity.Version);
        Assert.True(entity.IsCurrent);
        Assert.Equal("Hypertension", Assert.Single(entity.Conditions).Name);
        Assert.Equal("Active", Assert.Single(entity.Conditions).ClinicalStatus);
        Assert.Equal(entity.RecordId, created.RecordId);
        Assert.StartsWith("MedicalRecordCreated:", Assert.Single(fixture.Audits.Added).Action);
        Assert.Equal(1, fixture.UnitOfWork.SaveCount);
    }

    [Fact]
    public async Task Create_for_missing_patient_returns_not_found_without_writes()
    {
        var fixture = new Fixture(null);

        var result = await fixture.Service.CreateAsync(Guid.NewGuid(), CreateRequest(), Access);

        Assert.IsType<MedicalRecordCreatePatientNotFoundResult>(result);
        Assert.Empty(fixture.Records.Added);
        Assert.Empty(fixture.Audits.Added);
        Assert.Equal(0, fixture.UnitOfWork.SaveCount);
    }

    [Fact]
    public async Task List_for_missing_patient_returns_null_without_record_reads()
    {
        var fixture = new Fixture(null);

        var result = await fixture.Service.GetPageAsync(
            Guid.NewGuid(),
            new MedicalRecordListRequest(),
            Access);

        Assert.Null(result);
        Assert.Empty(fixture.Audits.Added);
        Assert.Equal(0, fixture.UnitOfWork.SaveCount);
    }

    [Fact]
    public async Task Update_retains_previous_version_and_creates_a_linked_snapshot()
    {
        var patient = ExistingPatient();
        var current = ExistingRecord(patient.Id);
        var fixture = new Fixture(patient, current);

        var result = await fixture.Service.UpdateAsync(
            patient.Id,
            current.RecordId,
            new UpdateMedicalRecordRequest(
                current.Version,
                "Updated clinical assessment.",
                [new("Asthma", "J45", "Resolved", null)]),
            Access);

        var updated = Assert.IsType<MedicalRecordUpdatedResult>(result).Record;
        var next = Assert.Single(fixture.Records.Added);
        Assert.False(current.IsCurrent);
        Assert.False(current.IsDeleted);
        Assert.Equal("Original clinical note.", current.ClinicalNotes);
        Assert.Equal(current.RecordId, next.RecordId);
        Assert.Equal(current.Id, next.PreviousVersionId);
        Assert.Equal(current.VisitReference, next.VisitReference);
        Assert.Equal(2, next.Version);
        Assert.True(next.IsCurrent);
        Assert.Equal("Updated clinical assessment.", next.ClinicalNotes);
        Assert.Equal(2, updated.Version);
        Assert.Contains(":v1-v2", Assert.Single(fixture.Audits.Added).Action);
        Assert.Equal(1, fixture.UnitOfWork.SaveCount);
    }

    [Fact]
    public async Task Update_with_stale_expected_version_returns_conflict_without_writes()
    {
        var patient = ExistingPatient();
        var current = ExistingRecord(patient.Id).WithVersion(3);
        var fixture = new Fixture(patient, current);

        var result = await fixture.Service.UpdateAsync(
            patient.Id,
            current.RecordId,
            new UpdateMedicalRecordRequest(2, "Stale update", []),
            Access);

        var conflict = Assert.IsType<MedicalRecordUpdateConflictResult>(result);
        Assert.Equal(3, conflict.CurrentVersion);
        Assert.True(current.IsCurrent);
        Assert.Empty(fixture.Records.Added);
        Assert.Empty(fixture.Audits.Added);
        Assert.Equal(0, fixture.UnitOfWork.SaveCount);
    }

    [Fact]
    public async Task Update_missing_record_returns_not_found_without_writes()
    {
        var fixture = new Fixture(ExistingPatient());

        var result = await fixture.Service.UpdateAsync(
            fixture.Patient!.Id,
            Guid.NewGuid(),
            new UpdateMedicalRecordRequest(1, "Updated notes", []),
            Access);

        Assert.IsType<MedicalRecordUpdateNotFoundResult>(result);
        Assert.Empty(fixture.Records.Added);
        Assert.Empty(fixture.Audits.Added);
        Assert.Equal(0, fixture.UnitOfWork.SaveCount);
    }

    [Fact]
    public async Task Concurrent_database_update_is_returned_as_version_conflict()
    {
        var patient = ExistingPatient();
        var current = ExistingRecord(patient.Id);
        var fixture = new Fixture(patient, current);
        fixture.UnitOfWork.SaveException = new MedicalRecordVersionConflictException();

        var result = await fixture.Service.UpdateAsync(
            patient.Id,
            current.RecordId,
            new UpdateMedicalRecordRequest(1, "Concurrent update", []),
            Access);

        Assert.IsType<MedicalRecordUpdateConflictResult>(result);
    }

    [Fact]
    public async Task Reading_record_writes_audit_entry()
    {
        var patient = ExistingPatient();
        var record = ExistingRecord(patient.Id);
        var fixture = new Fixture(patient, record);

        var result = await fixture.Service.GetByIdAsync(patient.Id, record.RecordId, Access);

        Assert.NotNull(result);
        var audit = Assert.Single(fixture.Audits.Added);
        Assert.Equal($"MedicalRecordViewed:{record.RecordId}:v1", audit.Action);
        Assert.Equal("corr-26", audit.CorrelationId);
        Assert.Equal(1, fixture.UnitOfWork.SaveCount);
    }

    [Fact]
    public async Task Reading_missing_record_does_not_write_false_audit()
    {
        var fixture = new Fixture(ExistingPatient());

        var result = await fixture.Service.GetByIdAsync(
            fixture.Patient!.Id,
            Guid.NewGuid(),
            Access);

        Assert.Null(result);
        Assert.Empty(fixture.Audits.Added);
        Assert.Equal(0, fixture.UnitOfWork.SaveCount);
    }

    [Fact]
    public async Task Paginated_read_audits_each_returned_record()
    {
        var patient = ExistingPatient();
        var first = ExistingRecord(patient.Id);
        var second = ExistingRecord(patient.Id);
        var fixture = new Fixture(patient, first);
        fixture.Records.PageItems = [first, second];

        var result = await fixture.Service.GetPageAsync(
            patient.Id,
            new MedicalRecordListRequest { Page = 1, PageSize = 1 },
            Access);

        Assert.NotNull(result);
        Assert.Equal(2, result.TotalCount);
        Assert.Equal(2, result.TotalPages);
        Assert.Equal("Original clinical note.", result.Items[0].ClinicalNotesPreview);
        Assert.Equal(1, result.Items[0].ConditionCount);
        Assert.Equal(2, fixture.Audits.Added.Count);
        Assert.Equal(1, fixture.UnitOfWork.SaveCount);
    }

    [Fact]
    public async Task Timeline_summary_normalizes_and_truncates_long_clinical_notes()
    {
        var patient = ExistingPatient();
        var record = ExistingRecord(patient.Id);
        record.ClinicalNotes = $"  {new string('A', 250)}\nfollow-up  ";
        var fixture = new Fixture(patient, record);

        var result = await fixture.Service.GetPageAsync(
            patient.Id,
            new MedicalRecordListRequest(),
            Access);

        Assert.NotNull(result);
        var preview = Assert.Single(result.Items).ClinicalNotesPreview;
        Assert.Equal(241, preview.Length);
        Assert.EndsWith("…", preview);
        Assert.DoesNotContain('\n', preview);
    }

    [Fact]
    public async Task Version_history_returns_all_snapshots_and_audits_every_read()
    {
        var patient = ExistingPatient();
        var current = ExistingRecord(patient.Id).WithVersion(2);
        var previous = ExistingRecord(patient.Id);
        previous.RecordId = current.RecordId;
        previous.IsCurrent = false;
        var fixture = new Fixture(patient, current);
        fixture.Records.Versions = [current, previous];

        var result = await fixture.Service.GetVersionsAsync(patient.Id, current.RecordId, Access);

        Assert.NotNull(result);
        Assert.Equal([2, 1], result.Select(version => version.Version));
        Assert.Equal(2, fixture.Audits.Added.Count);
        Assert.Equal(1, fixture.UnitOfWork.SaveCount);
    }

    [Fact]
    public async Task Missing_current_record_does_not_expose_old_versions()
    {
        var fixture = new Fixture(ExistingPatient());

        var result = await fixture.Service.GetVersionsAsync(
            fixture.Patient!.Id,
            Guid.NewGuid(),
            Access);

        Assert.Null(result);
        Assert.Empty(fixture.Audits.Added);
    }

    [Fact]
    public async Task Delete_soft_deletes_only_the_current_version_and_writes_audit()
    {
        var patient = ExistingPatient();
        var current = ExistingRecord(patient.Id);
        var fixture = new Fixture(patient, current);

        var deleted = await fixture.Service.DeleteAsync(patient.Id, current.RecordId, Access);

        Assert.True(deleted);
        Assert.True(current.IsDeleted);
        Assert.False(current.IsCurrent);
        Assert.Equal("clinician-26", current.UpdatedBy);
        Assert.StartsWith("MedicalRecordDeleted:", Assert.Single(fixture.Audits.Added).Action);
        Assert.Equal(1, fixture.UnitOfWork.SaveCount);
    }

    [Fact]
    public async Task Delete_missing_record_returns_false_without_writes()
    {
        var fixture = new Fixture(ExistingPatient());

        var deleted = await fixture.Service.DeleteAsync(
            fixture.Patient!.Id,
            Guid.NewGuid(),
            Access);

        Assert.False(deleted);
        Assert.Empty(fixture.Audits.Added);
        Assert.Equal(0, fixture.UnitOfWork.SaveCount);
    }

    private static CreateMedicalRecordRequest CreateRequest() =>
        new(Guid.NewGuid(), "Clinical notes", []);

    private static PatientEntity ExistingPatient() => new()
    {
        Id = Guid.NewGuid(),
        PatientNumber = "PAT-000026",
        Nic = "200012345678",
        FirstName = "Test",
        LastName = "Patient"
    };

    private static MedicalRecordEntity ExistingRecord(Guid patientId) => new()
    {
        PatientId = patientId,
        VisitReference = Guid.NewGuid(),
        ClinicalNotes = "Original clinical note.",
        AuthorClinicianId = "doctor-original",
        AuthorClinicianEmail = "original@medicore.lk",
        AuthorClinicianRole = "Doctor",
        AuthoredAtUtc = Now.AddDays(-1).UtcDateTime,
        CreatedBy = "doctor-original",
        Conditions =
        [
            new MediCore.Patient.Application.Entities.Condition
            {
                Name = "Migraine",
                ClinicalStatus = "Active",
                CreatedBy = "doctor-original"
            }
        ]
    };

    private sealed class Fixture
    {
        public Fixture(PatientEntity? patient, MedicalRecordEntity? current = null)
        {
            Patient = patient;
            Patients = new FakePatientRepository(patient);
            Records = new FakeMedicalRecordRepository(current);
            Audits = new FakeAuditRepository();
            UnitOfWork = new FakeUnitOfWork();
            Service = new MedicalRecordService(
                Patients,
                Records,
                Audits,
                UnitOfWork,
                new FixedTimeProvider(Now));
        }

        public PatientEntity? Patient { get; }
        public FakePatientRepository Patients { get; }
        public FakeMedicalRecordRepository Records { get; }
        public FakeAuditRepository Audits { get; }
        public FakeUnitOfWork UnitOfWork { get; }
        public MedicalRecordService Service { get; }
    }

    private sealed class FakePatientRepository(PatientEntity? patient) : IPatientRepository
    {
        public Task<PatientEntity?> FindByNicAsync(string normalizedNic, bool includeArchived, CancellationToken cancellationToken = default) =>
            Task.FromResult(patient);

        public Task AddAsync(PatientEntity value, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<PatientEntity?> GetByIdAsync(Guid patientId, CancellationToken cancellationToken = default) =>
            Task.FromResult(patient is { IsDeleted: false } && patient.Id == patientId ? patient : null);

        public Task<PatientEntity?> GetTrackedByIdAsync(Guid patientId, CancellationToken cancellationToken = default) =>
            GetByIdAsync(patientId, cancellationToken);
    }

    private sealed class FakeMedicalRecordRepository(MedicalRecordEntity? current) : IMedicalRecordRepository
    {
        public List<MedicalRecordEntity> Added { get; } = [];
        public IReadOnlyList<MedicalRecordEntity> PageItems { get; set; } = current is null ? [] : [current];
        public IReadOnlyList<MedicalRecordEntity> Versions { get; set; } = current is null ? [] : [current];

        public Task AddAsync(MedicalRecordEntity record, CancellationToken cancellationToken = default)
        {
            Added.Add(record);
            return Task.CompletedTask;
        }

        public Task<MedicalRecordEntity?> GetCurrentAsync(Guid patientId, Guid recordId, CancellationToken cancellationToken = default) =>
            Task.FromResult(current is { IsCurrent: true, IsDeleted: false } &&
                current.PatientId == patientId && current.RecordId == recordId ? current : null);

        public Task<MedicalRecordEntity?> GetTrackedCurrentAsync(Guid patientId, Guid recordId, CancellationToken cancellationToken = default) =>
            GetCurrentAsync(patientId, recordId, cancellationToken);

        public Task<(IReadOnlyList<MedicalRecordEntity> Items, int TotalCount)> GetCurrentPageAsync(
            Guid patientId,
            int page,
            int pageSize,
            CancellationToken cancellationToken = default) =>
            Task.FromResult((PageItems, PageItems.Count));

        public Task<IReadOnlyList<MedicalRecordEntity>> GetVersionsAsync(
            Guid patientId,
            Guid recordId,
            CancellationToken cancellationToken = default) => Task.FromResult(Versions);
    }

    private sealed class FakeAuditRepository : IPatientAuditRepository
    {
        public List<PatientAuditLog> Added { get; } = [];

        public Task AddAsync(PatientAuditLog auditLog, CancellationToken cancellationToken = default)
        {
            Added.Add(auditLog);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeUnitOfWork : IUnitOfWork
    {
        public int SaveCount { get; private set; }
        public Exception? SaveException { get; set; }

        public Task SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            SaveCount++;
            return SaveException is null ? Task.CompletedTask : Task.FromException(SaveException);
        }

        public Task SaveRegistrationAsync(string duplicateNic, CancellationToken cancellationToken = default) =>
            SaveChangesAsync(cancellationToken);
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}

internal static class MedicalRecordTestExtensions
{
    public static MedicalRecordEntity WithVersion(this MedicalRecordEntity record, int version)
    {
        record.Version = version;
        return record;
    }
}
