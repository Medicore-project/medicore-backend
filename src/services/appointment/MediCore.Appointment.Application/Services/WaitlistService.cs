using MediCore.Appointment.Application.Concurrency;
using MediCore.Appointment.Application.DTOs;
using MediCore.Appointment.Application.Entities;
using MediCore.Appointment.Application.Exceptions;
using MediCore.Appointment.Application.Interfaces;
using MediCore.Appointment.Application.Scheduling;
using Microsoft.Extensions.Options;

namespace MediCore.Appointment.Application.Services;

/// <inheritdoc cref="IWaitlistService"/>
public sealed class WaitlistService : IWaitlistService
{
    private readonly IWaitlistRepository _waitlistRepository;
    private readonly IDoctorCacheRepository _doctorRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _timeProvider;
    private readonly WaitlistOptions _options;
    private readonly SchedulingOptions _schedulingOptions;

    public WaitlistService(
        IWaitlistRepository waitlistRepository,
        IDoctorCacheRepository doctorRepository,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider,
        IOptions<WaitlistOptions> options,
        IOptions<SchedulingOptions> schedulingOptions)
    {
        _waitlistRepository = waitlistRepository;
        _doctorRepository = doctorRepository;
        _unitOfWork = unitOfWork;
        _timeProvider = timeProvider;
        _options = options.Value;
        _schedulingOptions = schedulingOptions.Value;
    }

    /// <remarks>
    /// Runs under the queue lock, like every write to a queue, so two joins cannot read the same
    /// highest position and the fullness check cannot go stale before the entry is written. A lost
    /// race — the position index firing, or a deadlock — re-runs the attempt, as booking does.
    /// </remarks>
    public async Task<WaitlistJoinResult> JoinAsync(
        Guid doctorId,
        DateOnly date,
        Guid patientId,
        BookingPatientDetails? patientDetails,
        string? serviceCode,
        string actor,
        CancellationToken cancellationToken = default)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await _unitOfWork.ExecuteInTransactionAsync(
                    token => AttemptJoinAsync(
                        doctorId, date, patientId, patientDetails, serviceCode, actor, token),
                    cancellationToken);
            }
            catch (DuplicateWaitlistEntryException)
            {
                return new WaitlistAlreadyWaitingResult();
            }
            catch (ConcurrentUpdateException) when (attempt < ConcurrencyRetry.MaxAttempts)
            {
                // Rolled back and the tracker cleared; read the queue again.
            }
            catch (ConcurrentUpdateException)
            {
                return new WaitlistJoinContendedResult();
            }
        }
    }

    private async Task<WaitlistJoinResult> AttemptJoinAsync(
        Guid doctorId,
        DateOnly date,
        Guid patientId,
        BookingPatientDetails? patientDetails,
        string? serviceCode,
        string actor,
        CancellationToken cancellationToken)
    {
        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var today = ColomboTime.ToColomboDate(nowUtc);

        if (date < today)
        {
            return new WaitlistDateInPastResult();
        }

        var horizonDays = _schedulingOptions.SlotHorizonDays;
        if (date > today.AddDays(horizonDays))
        {
            return new WaitlistBeyondHorizonResult(horizonDays);
        }

        var doctor = await _doctorRepository.GetActiveAsync(doctorId, cancellationToken);
        if (doctor is null)
        {
            return new WaitlistDoctorNotFoundResult();
        }

        await _waitlistRepository.LockQueueAsync(doctorId, date, cancellationToken);

        // The day itself first: "there is nothing to wait for" is the answer whoever is asking.
        var day = (await _waitlistRepository.GetDayAvailabilityAsync(
                doctorId, date, date, nowUtc, cancellationToken))
            .SingleOrDefault(availability => availability.Date == date);

        if (day is null)
        {
            return new WaitlistNoClinicThatDayResult();
        }

        if (!day.IsFull)
        {
            // Only a free slot can make a listed day not full: a day with nothing free and nothing
            // taken is never listed.
            return new WaitlistDayNotFullResult(day.Free);
        }

        if (await _waitlistRepository.HasActiveEntryAsync(patientId, doctorId, date, cancellationToken))
        {
            return new WaitlistAlreadyWaitingResult();
        }

        if (await _waitlistRepository.HasBookedWithDoctorOnAsync(patientId, doctorId, date, cancellationToken))
        {
            return new WaitlistAlreadyBookedResult();
        }

        var limit = _options.EffectiveMaxActiveEntriesPerPatient;
        if (await _waitlistRepository.CountActiveForPatientAsync(patientId, today, cancellationToken) >= limit)
        {
            return new WaitlistTooManyEntriesResult(limit);
        }

        var entry = new WaitlistEntry
        {
            DoctorId = doctorId,
            SlotDate = date,
            PatientId = patientId,
            PatientNumber = patientDetails?.PatientNumber,
            PatientName = patientDetails?.PatientName,
            ServiceCode = ServiceCodes.ResolveForDoctor(doctor.Specialization, serviceCode),
            Position = await _waitlistRepository.GetNextPositionAsync(doctorId, date, cancellationToken),
            Status = WaitlistStatus.Waiting,
            JoinedAtUtc = nowUtc,
            CreatedBy = actor
        };

        await _waitlistRepository.AddAsync(entry, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        // Read back inside the transaction, so the place in line counts exactly the entries ahead
        // of this one as the lock left them.
        var saved = await GetAsync(entry.WaitlistEntryId, cancellationToken)
            ?? throw new InvalidOperationException("A waitlist entry just saved could not be read back.");

        return new WaitlistJoinedResult(saved);
    }

    public async Task<IReadOnlyList<WaitlistEntryResponse>> GetMineAsync(
        Guid patientId,
        CancellationToken cancellationToken = default)
    {
        var closedSince = _timeProvider.GetUtcNow().UtcDateTime.AddDays(-IWaitlistService.RecentlyClosedDays);
        var listings = await _waitlistRepository.ListForPatientAsync(patientId, closedSince, cancellationToken);

        return await WithPlacesAsync(listings, cancellationToken);
    }

    public async Task<IReadOnlyList<WaitlistEntryResponse>> ListAsync(
        Guid? doctorId,
        DateOnly from,
        DateOnly to,
        string? status,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyCollection<string>? statuses = status switch
        {
            null => null,
            IWaitlistService.ActiveFilter => [WaitlistStatus.Waiting, WaitlistStatus.Offered],
            _ => [status]
        };

        var listings = await _waitlistRepository.ListAsync(doctorId, from, to, statuses, cancellationToken);

        return await WithPlacesAsync(listings, cancellationToken);
    }

    public async Task<WaitlistEntryResponse?> GetAsync(
        Guid waitlistEntryId,
        CancellationToken cancellationToken = default)
    {
        var listing = await _waitlistRepository.GetListingAsync(waitlistEntryId, cancellationToken);

        return listing is null
            ? null
            : (await WithPlacesAsync([listing], cancellationToken))[0];
    }

    public async Task<WaitlistDaysResult> GetDaysAsync(
        Guid doctorId,
        DateOnly? from,
        DateOnly? to,
        CancellationToken cancellationToken = default)
    {
        if (await _doctorRepository.GetActiveAsync(doctorId, cancellationToken) is null)
        {
            return new WaitlistDaysDoctorNotFoundResult();
        }

        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var today = ColomboTime.ToColomboDate(nowUtc);
        var horizon = today.AddDays(_schedulingOptions.SlotHorizonDays);

        // Clamped to what exists: nothing before today is bookable, nothing past the horizon is
        // generated yet. An anonymous caller cannot make this scan more than the horizon.
        var start = from is { } requestedFrom && requestedFrom > today ? requestedFrom : today;
        var end = to is { } requestedTo && requestedTo < horizon ? requestedTo : horizon;

        if (start > end)
        {
            return new WaitlistDaysFoundResult([]);
        }

        var days = await _waitlistRepository.GetDayAvailabilityAsync(
            doctorId, start, end, nowUtc, cancellationToken);

        return new WaitlistDaysFoundResult(days
            .OrderBy(day => day.Date)
            .Select(day => new PublicBookingDayResponse(day.Date, day.IsFull))
            .ToList());
    }

    /// <summary>
    /// Maps listings to responses with each waiting entry's place in line. One read of the waiting
    /// positions covers every queue in the list; a list with nobody waiting needs none.
    /// </summary>
    private async Task<IReadOnlyList<WaitlistEntryResponse>> WithPlacesAsync(
        IReadOnlyList<WaitlistListing> listings,
        CancellationToken cancellationToken)
    {
        var waitingEntries = listings
            .Select(listing => listing.Entry)
            .Where(entry => entry.Status == WaitlistStatus.Waiting)
            .ToList();

        IReadOnlyList<WaitingPosition> waiting = waitingEntries.Count == 0
            ? []
            : await _waitlistRepository.GetWaitingPositionsAsync(
                waitingEntries.Select(entry => entry.DoctorId).Distinct().ToList(),
                waitingEntries.Min(entry => entry.SlotDate),
                waitingEntries.Max(entry => entry.SlotDate),
                cancellationToken);

        return listings
            .Select(listing => WaitlistMapping.ToResponse(
                listing, WaitlistMapping.PlaceInLine(listing.Entry, waiting)))
            .ToList();
    }
}
