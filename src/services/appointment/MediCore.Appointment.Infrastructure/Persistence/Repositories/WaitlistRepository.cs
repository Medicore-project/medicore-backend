using MediCore.Appointment.Application.Entities;
using MediCore.Appointment.Application.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace MediCore.Appointment.Infrastructure.Persistence.Repositories;

/// <inheritdoc cref="IWaitlistRepository"/>
public sealed class WaitlistRepository : IWaitlistRepository
{
    /// <summary>Advisory-lock namespace for "one doctor's day's queue". Arbitrary but fixed.</summary>
    internal const int QueueLockNamespace = 37_001;

    private readonly AppointmentDbContext _dbContext;

    public WaitlistRepository(AppointmentDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task LockQueueAsync(Guid doctorId, DateOnly date, CancellationToken cancellationToken = default)
    {
        // The two-key form in its own namespace, as the patient lock does. Both values are bound.
        var key = QueueLockKey(doctorId, date);

        return _dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock({QueueLockNamespace}, {key})",
            cancellationToken);
    }

    /// <summary>
    /// A stable 32-bit key for one doctor's day: the patient lock's fold of the Guid, mixed with the
    /// day number. Deterministic arithmetic only — never <see cref="HashCode"/> or
    /// <see cref="object.GetHashCode"/>, which are seeded per process, so two instances of the
    /// service would lock different keys for the same queue. A collision only makes two queues wait
    /// for each other.
    /// </summary>
    internal static int QueueLockKey(Guid doctorId, DateOnly date) =>
        AppointmentRepository.PatientLockKey(doctorId) ^ unchecked(date.DayNumber * 397);

    public async Task<int> GetNextPositionAsync(
        Guid doctorId,
        DateOnly date,
        CancellationToken cancellationToken = default)
    {
        // IgnoreQueryFilters: a soft-deleted entry's position stays spent, as the unique index —
        // which excludes deleted rows — would otherwise allow it to be handed out again.
        var highest = await _dbContext.WaitlistEntries
            .IgnoreQueryFilters()
            .Where(entry => entry.DoctorId == doctorId && entry.SlotDate == date)
            .MaxAsync(entry => (int?)entry.Position, cancellationToken);

        return (highest ?? 0) + 1;
    }

    public Task<bool> HasActiveEntryAsync(
        Guid patientId,
        Guid doctorId,
        DateOnly date,
        CancellationToken cancellationToken = default)
    {
        return _dbContext.WaitlistEntries.AnyAsync(
            entry => entry.PatientId == patientId
                && entry.DoctorId == doctorId
                && entry.SlotDate == date
                && (entry.Status == WaitlistStatus.Waiting || entry.Status == WaitlistStatus.Offered),
            cancellationToken);
    }

    public Task<int> CountActiveForPatientAsync(
        Guid patientId,
        DateOnly from,
        CancellationToken cancellationToken = default)
    {
        return _dbContext.WaitlistEntries.CountAsync(
            entry => entry.PatientId == patientId
                && entry.SlotDate >= from
                && (entry.Status == WaitlistStatus.Waiting || entry.Status == WaitlistStatus.Offered),
            cancellationToken);
    }

    public Task<bool> HasBookedWithDoctorOnAsync(
        Guid patientId,
        Guid doctorId,
        DateOnly date,
        CancellationToken cancellationToken = default)
    {
        return _dbContext.Appointments.AnyAsync(
            appointment => appointment.PatientId == patientId
                && appointment.DoctorId == doctorId
                && appointment.SlotDate == date
                && appointment.Status == AppointmentStatus.Booked,
            cancellationToken);
    }

    public async Task<IReadOnlyList<DayAvailability>> GetDayAvailabilityAsync(
        Guid doctorId,
        DateOnly from,
        DateOnly to,
        DateTime nowUtc,
        CancellationToken cancellationToken = default)
    {
        // Served by ix_slots_doctor_date_status. StartUtc >= now matches the public availability
        // query, so a day this calls full never shows a free time on the booking page. Blocked and
        // Flagged slots count as neither: nobody can book them and nobody is waiting on them.
        var days = await _dbContext.Slots
            .AsNoTracking()
            .Where(slot =>
                slot.DoctorId == doctorId
                && slot.SlotDate >= from
                && slot.SlotDate <= to
                && slot.StartUtc >= nowUtc
                && (slot.Status == SlotStatus.Available
                    || slot.Status == SlotStatus.Booked
                    || slot.Status == SlotStatus.Offered))
            .GroupBy(slot => slot.SlotDate)
            .Select(day => new
            {
                Date = day.Key,
                Free = day.Count(slot => slot.Status == SlotStatus.Available),
                Taken = day.Count(slot => slot.Status != SlotStatus.Available)
            })
            .OrderBy(day => day.Date)
            .ToListAsync(cancellationToken);

        return days.Select(day => new DayAvailability(day.Date, day.Free, day.Taken)).ToList();
    }

    public async Task<IReadOnlyList<WaitlistEntry>> GetWaitingTrackedAsync(
        Guid doctorId,
        DateOnly date,
        CancellationToken cancellationToken = default)
    {
        // Served by ix_waitlist_entries_queue. Tracked: the offerer changes the one it picks, and
        // may withdraw some it passes over.
        return await _dbContext.WaitlistEntries
            .Where(entry =>
                entry.DoctorId == doctorId
                && entry.SlotDate == date
                && entry.Status == WaitlistStatus.Waiting)
            .OrderBy(entry => entry.Position)
            .ToListAsync(cancellationToken);
    }

    public Task AddAsync(WaitlistEntry entry, CancellationToken cancellationToken = default)
    {
        return _dbContext.WaitlistEntries.AddAsync(entry, cancellationToken).AsTask();
    }

    public async Task<WaitlistListing?> GetListingAsync(
        Guid waitlistEntryId,
        CancellationToken cancellationToken = default)
    {
        var row = await WithDetails(_dbContext.WaitlistEntries
                .AsNoTracking()
                .Where(entry => entry.WaitlistEntryId == waitlistEntryId))
            .SingleOrDefaultAsync(cancellationToken);

        return row?.ToListing();
    }

    public async Task<IReadOnlyList<WaitlistListing>> ListForPatientAsync(
        Guid patientId,
        DateTime closedSince,
        CancellationToken cancellationToken = default)
    {
        var entries = _dbContext.WaitlistEntries
            .AsNoTracking()
            .Where(entry =>
                entry.PatientId == patientId
                && (entry.Status == WaitlistStatus.Waiting
                    || entry.Status == WaitlistStatus.Offered
                    || entry.ClosedAtUtc >= closedSince));

        var rows = await WithDetails(entries)
            .OrderBy(row => row.Entry.SlotDate)
            .ThenBy(row => row.Entry.JoinedAtUtc)
            .ToListAsync(cancellationToken);

        return rows.Select(row => row.ToListing()).ToList();
    }

    public async Task<IReadOnlyList<WaitlistListing>> ListAsync(
        Guid? doctorId,
        DateOnly from,
        DateOnly to,
        IReadOnlyCollection<string>? statuses,
        CancellationToken cancellationToken = default)
    {
        var entries = _dbContext.WaitlistEntries
            .AsNoTracking()
            .Where(entry => entry.SlotDate >= from && entry.SlotDate <= to);

        if (doctorId is not null)
        {
            entries = entries.Where(entry => entry.DoctorId == doctorId);
        }

        if (statuses is not null)
        {
            entries = entries.Where(entry => statuses.Contains(entry.Status));
        }

        var rows = await WithDetails(entries)
            .OrderBy(row => row.Entry.SlotDate)
            .ThenBy(row => row.DoctorName)
            .ThenBy(row => row.Entry.Position)
            .ToListAsync(cancellationToken);

        return rows.Select(row => row.ToListing()).ToList();
    }

    public async Task<IReadOnlyList<WaitingPosition>> GetWaitingPositionsAsync(
        IReadOnlyCollection<Guid> doctorIds,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken = default)
    {
        // Served by ix_waitlist_entries_queue.
        return await _dbContext.WaitlistEntries
            .AsNoTracking()
            .Where(entry =>
                doctorIds.Contains(entry.DoctorId)
                && entry.SlotDate >= from
                && entry.SlotDate <= to
                && entry.Status == WaitlistStatus.Waiting)
            .Select(entry => new WaitingPosition(entry.DoctorId, entry.SlotDate, entry.Position))
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Left-joins the doctor cache for a name and the slots for the offered time. Left joins
    /// because neither has a foreign key: the cache is eventually consistent, and schedule revision
    /// may hard-delete an offered slot. Soft-deleted slots are filtered out as usual.
    /// </summary>
    private IQueryable<ListingRow> WithDetails(IQueryable<WaitlistEntry> entries) =>
        from entry in entries
        join doctor in _dbContext.DoctorCaches on entry.DoctorId equals doctor.DoctorId into doctors
        from doctor in doctors.DefaultIfEmpty()
        join slot in _dbContext.Slots on entry.OfferedSlotId equals (Guid?)slot.SlotId into slots
        from slot in slots.DefaultIfEmpty()
        select new ListingRow
        {
            Entry = entry,
            DoctorName = doctor == null ? null : doctor.FullName,
            DoctorSpecialization = doctor == null ? null : doctor.Specialization,
            OfferedStartUtc = slot == null ? null : slot.StartUtc,
            OfferedEndUtc = slot == null ? null : slot.EndUtc
        };

    /// <summary>
    /// The joined row while it is still a query — an object initializer, not the record's
    /// constructor, so EF can order by its members (see <c>AppointmentRepository</c>).
    /// </summary>
    private sealed class ListingRow
    {
        public required WaitlistEntry Entry { get; init; }
        public string? DoctorName { get; init; }
        public string? DoctorSpecialization { get; init; }
        public DateTime? OfferedStartUtc { get; init; }
        public DateTime? OfferedEndUtc { get; init; }

        public WaitlistListing ToListing() =>
            new(Entry, DoctorName, DoctorSpecialization, OfferedStartUtc, OfferedEndUtc);
    }
}
