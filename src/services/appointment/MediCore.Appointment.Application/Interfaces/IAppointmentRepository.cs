using AppointmentEntity = MediCore.Appointment.Application.Entities.Appointment;

namespace MediCore.Appointment.Application.Interfaces;

/// <summary>
/// An appointment together with its doctor's name and specialization from the doctor cache —
/// null when the cache holds no row for that doctor at all.
/// </summary>
/// <remarks>
/// The cache row is joined whatever its <c>IsActive</c> flag: a doctor who has since left the
/// clinic still saw the patients booked with them, and the list should still say who.
/// </remarks>
public sealed record AppointmentListing(
    AppointmentEntity Appointment,
    string? DoctorName,
    string? DoctorSpecialization);

/// <summary>Data-access contract for <see cref="AppointmentEntity"/>.</summary>
public interface IAppointmentRepository
{
    /// <summary>Stages a new appointment for insertion (not yet committed).</summary>
    Task AddAsync(AppointmentEntity appointment, CancellationToken cancellationToken = default);

    /// <summary>
    /// Takes a transaction-scoped lock on one patient's bookings, waiting while another
    /// transaction holds it. Released automatically when the transaction commits or rolls back,
    /// so it must be called inside <see cref="IUnitOfWork.ExecuteInTransactionAsync{T}"/>.
    /// </summary>
    /// <remarks>
    /// SCRUM-35. The overlap check reads, then the booking writes; two bookings for one patient at
    /// overlapping times could both read "no clash" and both write, because a time-range overlap
    /// cannot be a unique index. Holding this lock from before the read until the commit makes the
    /// second booking read after the first has committed, so it sees the clash.
    /// </remarks>
    Task LockPatientAsync(Guid patientId, CancellationToken cancellationToken = default);

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
    /// <para>
    /// <paramref name="excludeAppointmentId"/> leaves one appointment out — the one a reschedule is
    /// moving, which would otherwise clash with its own new time whenever the two overlap.
    /// </para>
    /// </remarks>
    Task<AppointmentEntity?> FindPatientOverlapAsync(
        Guid patientId,
        DateTime startUtc,
        DateTime endUtc,
        Guid? excludeAppointmentId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Every appointment whose Colombo date falls in <paramref name="from"/>..<paramref name="to"/>
    /// inclusive, whatever its status, soonest first. Narrowed to one doctor when
    /// <paramref name="doctorId"/> is given, otherwise the whole clinic.
    /// </summary>
    Task<IReadOnlyList<AppointmentListing>> ListAsync(
        Guid? doctorId,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// One patient's still-booked appointments starting after <paramref name="nowUtc"/>, soonest
    /// first, capped at <paramref name="limit"/>.
    /// </summary>
    Task<IReadOnlyList<AppointmentListing>> ListUpcomingForPatientAsync(
        Guid patientId,
        DateTime nowUtc,
        int limit,
        CancellationToken cancellationToken = default);

    /// <summary>Returns an appointment by its business key, for reading.</summary>
    Task<AppointmentEntity?> GetByAppointmentIdAsync(
        Guid appointmentId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns an appointment by its business key, tracked for change, and locks its row until the
    /// transaction ends. Must be called inside
    /// <see cref="IUnitOfWork.ExecuteInTransactionAsync{T}"/>.
    /// </summary>
    /// <remarks>
    /// SCRUM-36. Every change to an appointment starts here, so two changes to the same appointment
    /// run one after the other: the second waits, then reads what the first committed. Without it
    /// a cancel and a complete arriving together could both see <c>Booked</c> and both succeed,
    /// leaving a completed appointment on a released slot. A row lock rather than an
    /// <c>xmin</c> token, so waiting replaces failing and retrying, and the SCRUM-35 rule that only
    /// the slot carries a token stands.
    /// </remarks>
    Task<AppointmentEntity?> GetTrackedForUpdateAsync(
        Guid appointmentId,
        CancellationToken cancellationToken = default);
}
