using AppointmentEntity = MediCore.Appointment.Application.Entities.Appointment;

namespace MediCore.Appointment.Application.Interfaces;

/// <summary>Data-access contract for <see cref="AppointmentEntity"/>.</summary>
public interface IAppointmentRepository
{
    /// <summary>Stages a new appointment for insertion (not yet committed).</summary>
    Task AddAsync(AppointmentEntity appointment, CancellationToken cancellationToken = default);

    /// <summary>
    /// An active booking for this patient whose time window intersects
    /// <paramref name="startUtc"/>..<paramref name="endUtc"/>, or null if the patient is free.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Intervals are half-open — <c>[Start, End)</c> — so two back-to-back slots do not clash. Slot
    /// generation produces contiguous slots where one slot's end is the next one's start, and
    /// booking both is a legitimate way to get a longer consultation. A closed comparison would
    /// refuse it.
    /// </para>
    /// <para>
    /// Deliberately not narrowed to one doctor: the clash is that the patient cannot be in two
    /// places at once, not that a particular doctor is busy. Only
    /// <c>AppointmentStatus.Booked</c> rows block — a cancellation released the time, and a
    /// completed visit is in the past, which the past-slot guard has already rejected.
    /// </para>
    /// <para>
    /// Answerable entirely from this service's own tables: it needs what the patient already
    /// booked, never who they are, so nothing crosses a service boundary.
    /// </para>
    /// </remarks>
    Task<AppointmentEntity?> FindPatientOverlapAsync(
        Guid patientId,
        DateTime startUtc,
        DateTime endUtc,
        CancellationToken cancellationToken = default);

    /// <summary>Returns an appointment by its business key, for reading.</summary>
    Task<AppointmentEntity?> GetByAppointmentIdAsync(
        Guid appointmentId,
        CancellationToken cancellationToken = default);
}
