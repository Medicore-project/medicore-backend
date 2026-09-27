using MediCore.Appointment.Application.Exceptions;
using MediCore.Appointment.Infrastructure.Persistence;
using MediCore.Appointment.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace MediCore.Appointment.Tests.Unit;

/// <summary>
/// SCRUM-35: which database failures count as "someone got in the way, run it again", and the
/// key the per-patient booking lock is taken on. SCRUM-37: the same for the waitlist's indexes and
/// its per-queue lock.
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

    [Fact]
    public void A_second_active_waitlist_entry_for_a_patient_means_they_are_already_waiting()
    {
        var translated = AppointmentUnitOfWork.TranslateWaitlistViolation(
            "ux_waitlist_entries_patient_active", new InvalidOperationException());

        Assert.IsType<DuplicateWaitlistEntryException>(translated);
    }

    [Theory]
    [InlineData("ux_waitlist_entries_queue_position")]
    [InlineData("ux_waitlist_entries_offered_slot")]
    public void A_collision_on_a_queue_position_or_an_offered_slot_is_retried(string constraintName)
    {
        // Another writer to the queue got in the way; the operation re-reads and runs again.
        var translated = AppointmentUnitOfWork.TranslateWaitlistViolation(
            constraintName, new InvalidOperationException());

        Assert.IsType<ConcurrentUpdateException>(translated);
    }

    [Theory]
    [InlineData("ux_waitlist_entries_entry_id")]
    [InlineData("ux_appointments_slot")]
    [InlineData(null)]
    public void Any_other_index_is_not_the_waitlists_to_translate(string? constraintName)
    {
        Assert.Null(AppointmentUnitOfWork.TranslateWaitlistViolation(
            constraintName, new InvalidOperationException()));
    }

    [Fact]
    public void The_queue_lock_key_is_stable_for_one_doctors_day()
    {
        // Every writer to a queue, in every process, must wait on the same lock.
        var doctorId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var day = new DateOnly(2026, 9, 25);

        Assert.Equal(
            WaitlistRepository.QueueLockKey(doctorId, day),
            WaitlistRepository.QueueLockKey(Guid.Parse(doctorId.ToString()), new DateOnly(2026, 9, 25)));
    }

    [Fact]
    public void A_doctors_days_lock_separately()
    {
        var doctorId = Guid.NewGuid();
        var keys = Enumerable.Range(0, 366)
            .Select(offset => WaitlistRepository.QueueLockKey(doctorId, new DateOnly(2026, 1, 1).AddDays(offset)))
            .ToHashSet();

        Assert.Equal(366, keys.Count);
    }

    [Fact]
    public void Different_doctors_on_one_day_almost_always_lock_separately()
    {
        var day = new DateOnly(2026, 9, 25);
        var keys = Enumerable.Range(0, 10_000)
            .Select(_ => WaitlistRepository.QueueLockKey(Guid.NewGuid(), day))
            .ToHashSet();

        Assert.True(keys.Count > 9_990);
    }

    [Fact]
    public void The_queue_lock_lives_apart_from_the_patient_lock()
    {
        Assert.NotEqual(AppointmentRepository.PatientBookingLockNamespace, WaitlistRepository.QueueLockNamespace);
    }

    private static PostgresException Postgres(string sqlState) =>
        new("simulated", "ERROR", "ERROR", sqlState);
}
