using MediCore.Appointment.Application.DTOs;
using MediCore.Appointment.Application.Entities;
using MediCore.Appointment.Application.Interfaces;
using MediCore.Appointment.Application.Scheduling;

namespace MediCore.Appointment.Application.Services;

/// <inheritdoc cref="ISlotService"/>
public sealed class SlotService : ISlotService
{
    private readonly ISlotRepository _repository;
    private readonly ISlotGenerator _slotGenerator;
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _timeProvider;

    public SlotService(
        ISlotRepository repository,
        ISlotGenerator slotGenerator,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider)
    {
        _repository = repository;
        _slotGenerator = slotGenerator;
        _unitOfWork = unitOfWork;
        _timeProvider = timeProvider;
    }

    public async Task<IReadOnlyList<SlotResponse>> GetAvailableAsync(
        Guid doctorId,
        DateOnly? from,
        DateOnly? to,
        CancellationToken cancellationToken = default)
    {
        // An omitted range means "as far ahead as slots exist", which is the horizon.
        var (defaultFrom, defaultTo) = _slotGenerator.CurrentHorizon();

        var slots = await _repository.GetAvailableAsync(
            doctorId,
            from ?? defaultFrom,
            to ?? defaultTo,
            _timeProvider.GetUtcNow().UtcDateTime,
            cancellationToken);

        return slots.Select(ToResponse).ToList();
    }

    public async Task<IReadOnlyList<SlotResponse>> GetFlaggedAsync(
        Guid? doctorId,
        CancellationToken cancellationToken = default)
    {
        var slots = await _repository.GetFlaggedAsync(doctorId, cancellationToken);
        return slots.Select(ToResponse).ToList();
    }

    public async Task<SlotBlockResult> BlockAsync(
        Guid slotId,
        BlockSlotRequest request,
        string actor,
        CancellationToken cancellationToken = default)
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

    public async Task<SlotUnblockResult> UnblockAsync(
        Guid slotId,
        string actor,
        CancellationToken cancellationToken = default)
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
