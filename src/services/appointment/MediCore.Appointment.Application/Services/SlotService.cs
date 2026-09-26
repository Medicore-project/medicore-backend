using MediCore.Appointment.Application.Concurrency;
using MediCore.Appointment.Application.DTOs;
using MediCore.Appointment.Application.Entities;
using MediCore.Appointment.Application.Interfaces;
using MediCore.Appointment.Application.Scheduling;

namespace MediCore.Appointment.Application.Services;

/// <inheritdoc cref="ISlotService"/>
public sealed class SlotService : ISlotService
{
    private readonly ISlotRepository _repository;
    private readonly IDoctorCacheRepository _doctorRepository;
    private readonly ISlotGenerator _slotGenerator;
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _timeProvider;

    public SlotService(
        ISlotRepository repository,
        IDoctorCacheRepository doctorRepository,
        ISlotGenerator slotGenerator,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider)
    {
        _repository = repository;
        _doctorRepository = doctorRepository;
        _slotGenerator = slotGenerator;
        _unitOfWork = unitOfWork;
        _timeProvider = timeProvider;
    }

    public async Task<AvailableSlotsResult> GetAvailableAsync(
        Guid doctorId,
        DateOnly? from,
        DateOnly? to,
        CancellationToken cancellationToken = default)
    {
        // A deactivated doctor keeps their slot rows, so without this check their free slots would
        // still be offered for booking.
        if (await _doctorRepository.GetActiveAsync(doctorId, cancellationToken) is null)
        {
            return new AvailableSlotsDoctorNotFoundResult();
        }

        // An omitted range means "as far ahead as slots exist", which is the horizon.
        var (defaultFrom, defaultTo) = _slotGenerator.CurrentHorizon();

        var slots = await _repository.GetAvailableAsync(
            doctorId,
            from ?? defaultFrom,
            to ?? defaultTo,
            _timeProvider.GetUtcNow().UtcDateTime,
            cancellationToken);

        return new AvailableSlotsFoundResult(slots.Select(ToResponse).ToList());
    }

    public async Task<IReadOnlyList<SlotResponse>> GetFlaggedAsync(
        Guid? doctorId,
        CancellationToken cancellationToken = default)
    {
        var slots = await _repository.GetFlaggedAsync(doctorId, cancellationToken);
        return slots.Select(ToResponse).ToList();
    }

    // SCRUM-35: blocking and unblocking race a booking for the same slot row. The slot's
    // concurrency token makes the later save lose; the retry then re-reads the slot and answers
    // from what it is now — typically "not available; it is Booked" — rather than overwriting it.
    public Task<SlotBlockResult> BlockAsync(
        Guid slotId,
        BlockSlotRequest request,
        string actor,
        CancellationToken cancellationToken = default) =>
        ConcurrencyRetry.RunAsync(
            token => BlockOnceAsync(slotId, request, actor, token),
            cancellationToken);

    public Task<SlotUnblockResult> UnblockAsync(
        Guid slotId,
        string actor,
        CancellationToken cancellationToken = default) =>
        ConcurrencyRetry.RunAsync(
            token => UnblockOnceAsync(slotId, actor, token),
            cancellationToken);

    private async Task<SlotBlockResult> BlockOnceAsync(
        Guid slotId,
        BlockSlotRequest request,
        string actor,
        CancellationToken cancellationToken)
    {
        var slot = await _repository.GetTrackedBySlotIdAsync(slotId, cancellationToken);

        if (slot is null)
        {
            return new SlotBlockNotFoundResult();
        }

        if (!string.Equals(slot.Status, SlotStatus.Available, StringComparison.Ordinal))
        {
            return new SlotBlockNotAvailableResult(slot.Status);
        }

        slot.Status = SlotStatus.Blocked;
        slot.UpdatedBy = actor;

        // Reused rather than adding a Blocked-specific column: both fields mean "why is this slot
        // not bookable, and since when".
        slot.FlaggedReason = request.Reason;
        slot.FlaggedAtUtc = _timeProvider.GetUtcNow().UtcDateTime;

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new SlotBlockedResult(ToResponse(slot));
    }

    private async Task<SlotUnblockResult> UnblockOnceAsync(
        Guid slotId,
        string actor,
        CancellationToken cancellationToken)
    {
        var slot = await _repository.GetTrackedBySlotIdAsync(slotId, cancellationToken);

        if (slot is null)
        {
            return new SlotUnblockNotFoundResult();
        }

        if (!string.Equals(slot.Status, SlotStatus.Blocked, StringComparison.Ordinal))
        {
            return new SlotUnblockNotBlockedResult(slot.Status);
        }

        slot.Status = SlotStatus.Available;
        slot.FlaggedReason = null;
        slot.FlaggedAtUtc = null;
        slot.UpdatedBy = actor;

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new SlotUnblockedResult(ToResponse(slot));
    }

    private static SlotResponse ToResponse(Slot slot) => new(
        slot.SlotId,
        slot.DoctorId,
        slot.DoctorScheduleId,
        slot.StartUtc,
        slot.EndUtc,
        slot.SlotDate,
        slot.DurationMinutes,
        slot.Status,
        slot.FlaggedReason,
        slot.FlaggedAtUtc);
}
