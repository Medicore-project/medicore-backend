using MediCore.Appointment.Infrastructure.Persistence;
using MediCore.Appointment.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace MediCore.Appointment.Tests.Unit;

/// <summary>
/// SCRUM-35: which database failures count as "someone got in the way, run it again", and the
/// key the per-patient booking lock is taken on.
/// </summary>
public sealed class AppointmentUnitOfWorkTests
{
    [Theory]
    [InlineData(PostgresErrorCodes.DeadlockDetected)]
    [InlineData(PostgresErrorCodes.SerializationFailure)]
    public void A_deadlock_or_serialization_failure_is_a_transient_conflict(string sqlState)
    {
        Assert.True(AppointmentUnitOfWork.IsTransientConflict(Postgres(sqlState)));
    }

    [Fact]
    public void It_is_recognised_inside_the_exception_EF_wraps_a_failed_save_in()
    {
        var wrapped = new DbUpdateException("save failed", Postgres(PostgresErrorCodes.DeadlockDetected));

        Assert.True(AppointmentUnitOfWork.IsTransientConflict(wrapped));
    }

    [Theory]
    [InlineData(PostgresErrorCodes.UniqueViolation)]
    [InlineData(PostgresErrorCodes.ForeignKeyViolation)]
    [InlineData(PostgresErrorCodes.InvalidPassword)]
    public void Anything_else_from_postgres_is_not_retried(string sqlState)
    {
        // A unique violation is an answer, and a bad password will not improve on the third try.
        Assert.False(AppointmentUnitOfWork.IsTransientConflict(Postgres(sqlState)));
    }

    [Fact]
    public void An_exception_that_did_not_come_from_postgres_is_not_retried()
    {
        Assert.False(AppointmentUnitOfWork.IsTransientConflict(new InvalidOperationException()));
    }

    [Fact]
    public void The_patient_lock_key_is_stable_for_one_patient()
    {
        // Every booking for a patient must queue on the same lock, across requests and processes.
        var patientId = Guid.Parse("22222222-2222-2222-2222-222222222222");

        Assert.Equal(
            AppointmentRepository.PatientLockKey(patientId),
            AppointmentRepository.PatientLockKey(Guid.Parse(patientId.ToString())));
    }

    [Fact]
    public void Different_patients_almost_always_get_different_lock_keys()
    {
        // A collision is harmless — two patients queue behind each other — but it should be rare,
        // or the lock would throttle unrelated bookings.
        var keys = Enumerable.Range(0, 10_000)
            .Select(_ => AppointmentRepository.PatientLockKey(Guid.NewGuid()))
            .ToHashSet();

        Assert.True(keys.Count > 9_990);
    }

    private static PostgresException Postgres(string sqlState) =>
        new("simulated", "ERROR", "ERROR", sqlState);
}
