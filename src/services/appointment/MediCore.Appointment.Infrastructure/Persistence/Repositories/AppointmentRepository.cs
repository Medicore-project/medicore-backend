using MediCore.Appointment.Application.Entities;
using MediCore.Appointment.Application.Interfaces;
using Microsoft.EntityFrameworkCore;
using AppointmentEntity = MediCore.Appointment.Application.Entities.Appointment;

namespace MediCore.Appointment.Infrastructure.Persistence.Repositories;

/// <inheritdoc cref="IAppointmentRepository"/>
public sealed class AppointmentRepository : IAppointmentRepository
{
    private readonly AppointmentDbContext _dbContext;

    public AppointmentRepository(AppointmentDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task AddAsync(AppointmentEntity appointment, CancellationToken cancellationToken = default)
    {
        return _dbContext.Appointments.AddAsync(appointment, cancellationToken).AsTask();
    }

    public Task LockPatientAsync(Guid patientId, CancellationToken cancellationToken = default)
    {
        // The two-key form, so the lock lives in its own namespace: Postgres advisory locks are
        // database-wide, and every service shares this database. The key folds the Guid to 32
        // bits; two patients colliding only means their bookings queue behind each other, which
        // is slower, never wrong. Both values are bound as parameters.
        var key = PatientLockKey(patientId);

        return _dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock({PatientBookingLockNamespace}, {key})",
            cancellationToken);
    }

    /// <summary>Advisory-lock namespace for "one patient's bookings". Arbitrary but fixed.</summary>
    internal const int PatientBookingLockNamespace = 35_001;

    /// <summary>A stable 32-bit fold of the patient id: the same Guid always gives the same key.</summary>
    internal static int PatientLockKey(Guid patientId)
    {
        var bytes = patientId.ToByteArray();
        return BitConverter.ToInt32(bytes, 0)
            ^ BitConverter.ToInt32(bytes, 4)
            ^ BitConverter.ToInt32(bytes, 8)
            ^ BitConverter.ToInt32(bytes, 12);
    }

    public Task<AppointmentEntity?> FindPatientOverlapAsync(
        Guid patientId,
        DateTime startUtc,
        DateTime endUtc,
        Guid? excludeAppointmentId = null,
        CancellationToken cancellationToken = default)
    {
        // Half-open intervals: strict comparisons on both sides, so an appointment ending exactly
        // when this one starts is not a clash. See the interface for why that matters.
        // Soft-deleted rows are excluded by the global query filter.
        return _dbContext.Appointments
            .AsNoTracking()
            .Where(appointment =>
                appointment.PatientId == patientId
                && appointment.Status == AppointmentStatus.Booked
                && appointment.StartUtc < endUtc
                && appointment.EndUtc > startUtc
                && appointment.AppointmentId != excludeAppointmentId)
            .OrderBy(appointment => appointment.StartUtc)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<AppointmentListing>> ListAsync(
        Guid? doctorId,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken = default)
    {
        var appointments = _dbContext.Appointments
            .AsNoTracking()
            .Where(appointment => appointment.SlotDate >= from && appointment.SlotDate <= to);

        if (doctorId is not null)
        {
            appointments = appointments.Where(appointment => appointment.DoctorId == doctorId);
        }

        var rows = await WithDoctors(appointments)
            .OrderBy(row => row.Appointment.StartUtc)
            .ThenBy(row => row.DoctorName)
            .ToListAsync(cancellationToken);

        return rows.Select(row => row.ToListing()).ToList();
    }

    public async Task<IReadOnlyList<AppointmentListing>> ListUpcomingForPatientAsync(
        Guid patientId,
        DateTime nowUtc,
        int limit,
        CancellationToken cancellationToken = default)
    {
        // Served by ix_appointments_patient_start, the index the overlap check already uses.
        var appointments = _dbContext.Appointments
            .AsNoTracking()
            .Where(appointment =>
                appointment.PatientId == patientId
                && appointment.Status == AppointmentStatus.Booked
                && appointment.StartUtc > nowUtc);

        var rows = await WithDoctors(appointments)
            .OrderBy(row => row.Appointment.StartUtc)
            .Take(limit)
            .ToListAsync(cancellationToken);

        return rows.Select(row => row.ToListing()).ToList();
    }

    /// <summary>
    /// Left-joins the doctor cache for a display name. A left join because the cache is eventually
    /// consistent and there is no foreign key, so an appointment must never drop out of a list for
    /// want of a cache row. <c>IsActive</c> is deliberately not filtered — see
    /// <see cref="AppointmentListing"/>.
    /// </summary>
    private IQueryable<ListingRow> WithDoctors(IQueryable<AppointmentEntity> appointments) =>
        from appointment in appointments
        join doctor in _dbContext.DoctorCaches on appointment.DoctorId equals doctor.DoctorId into doctors
        from doctor in doctors.DefaultIfEmpty()
        select new ListingRow
        {
            Appointment = appointment,
            DoctorName = doctor == null ? null : doctor.FullName,
            DoctorSpecialization = doctor == null ? null : doctor.Specialization
        };

    /// <summary>
    /// The joined row while it is still a query. An object initializer rather than the
    /// <see cref="AppointmentListing"/> record's constructor, because EF can order by a member it
    /// assigned but not by one a constructor received — sorting a record projection fails to
    /// translate at runtime.
    /// </summary>
    private sealed class ListingRow
    {
        public required AppointmentEntity Appointment { get; init; }
        public string? DoctorName { get; init; }
        public string? DoctorSpecialization { get; init; }

        public AppointmentListing ToListing() =>
            new(Appointment, DoctorName, DoctorSpecialization);
    }

    public Task<AppointmentEntity?> GetByAppointmentIdAsync(
        Guid appointmentId,
        CancellationToken cancellationToken = default)
    {
        return _dbContext.Appointments
            .AsNoTracking()
            .SingleOrDefaultAsync(
                appointment => appointment.AppointmentId == appointmentId,
                cancellationToken);
    }

    /// <summary>A compile-time constant: nothing but the bound id varies.</summary>
    internal const string LockAppointmentSql =
        "SELECT * FROM " + AppointmentDbContext.SchemaName + ".appointments "
        + "WHERE \"AppointmentId\" = {0} FOR UPDATE";

    public Task<AppointmentEntity?> GetTrackedForUpdateAsync(
        Guid appointmentId,
        CancellationToken cancellationToken = default)
    {
        // Raw SQL only for FOR UPDATE, which LINQ cannot express. The schema is a constant and the
        // id is bound as a parameter. EF wraps this as a subquery to apply the soft-delete filter,
        // and Postgres allows FOR UPDATE there.
        return _dbContext.Appointments
            .FromSqlRaw(
                LockAppointmentSql,
                appointmentId)
            .SingleOrDefaultAsync(cancellationToken);
    }
}
