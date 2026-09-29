using MediCore.Appointment.Application.Entities;
using MediCore.Appointment.Application.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace MediCore.Appointment.Infrastructure.Persistence.Repositories;

/// <summary>
/// EF Core implementation of <see cref="ISlotRepository"/>. The global
/// <c>HasQueryFilter(!IsDeleted)</c> excludes soft-deleted rows from every query here.
/// </summary>
public sealed class SlotRepository : ISlotRepository
{
    private readonly AppointmentDbContext _dbContext;

    public SlotRepository(AppointmentDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IReadOnlyList<Slot>> GetTrackedForDoctorBetweenAsync(
        Guid doctorId,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken = default) =>
        await _dbContext.Slots
            // Tracked: reconciliation flags and deletes rows from this result.
            // Filtering on SlotDate rather than StartUtc keeps this a plain date comparison against
            // the denormalised column, instead of shifting the window across the Colombo offset.
            .Where(slot => slot.DoctorId == doctorId && slot.SlotDate >= from && slot.SlotDate <= to)
            .ToListAsync(cancellationToken);

    public Task AddRangeAsync(
        IReadOnlyCollection<Slot> slots,
        CancellationToken cancellationToken = default) =>
        _dbContext.Slots.AddRangeAsync(slots, cancellationToken);

    public void RemoveRange(IReadOnlyCollection<Slot> slots) =>
        _dbContext.Slots.RemoveRange(slots);

    public async Task<IReadOnlyList<Slot>> GetAvailableAsync(
        Guid doctorId,
        DateOnly from,
        DateOnly to,
        DateTime nowUtc,
        CancellationToken cancellationToken = default) =>
        await _dbContext.Slots
            .AsNoTracking()
            .Where(slot =>
                slot.DoctorId == doctorId
                && slot.SlotDate >= from
                && slot.SlotDate <= to
                && slot.Status == SlotStatus.Available
                // A slot whose start has passed is not bookable, even though its date is in range.
                && slot.StartUtc >= nowUtc)
            .OrderBy(slot => slot.StartUtc)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Slot>> GetFlaggedAsync(
        Guid? doctorId,
        CancellationToken cancellationToken = default) =>
        await _dbContext.Slots
            .AsNoTracking()
            .Where(slot =>
                slot.Status == SlotStatus.Flagged
                && (doctorId == null || slot.DoctorId == doctorId))
            .OrderBy(slot => slot.StartUtc)
            .ToListAsync(cancellationToken);

    public Task<Slot?> GetTrackedBySlotIdAsync(
        Guid slotId,
        CancellationToken cancellationToken = default) =>
        _dbContext.Slots.SingleOrDefaultAsync(slot => slot.SlotId == slotId, cancellationToken);
}
