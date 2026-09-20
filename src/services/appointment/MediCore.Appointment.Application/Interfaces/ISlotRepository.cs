using MediCore.Appointment.Application.Entities;

namespace MediCore.Appointment.Application.Interfaces;

/// <summary>Data-access contract for <see cref="Slot"/>.</summary>
public interface ISlotRepository
{
    /// <summary>
    /// Change-tracked slots for one doctor whose Colombo date falls in
    /// <paramref name="from"/>..<paramref name="to"/>.
    /// </summary>
    /// <remarks>
    /// Tracked on purpose: reconciliation flags some of these rows and deletes others, both of
    /// which need the change tracker. Soft-deleted rows are excluded by the global query filter.
    /// <para>
    /// Flagged slots are included in the result even though reconciliation never mutates them —
    /// the caller needs to see them to know they exist.
    /// </para>
    /// </remarks>
    Task<IReadOnlyList<Slot>> GetTrackedForDoctorBetweenAsync(
        Guid doctorId,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken = default);

    /// <summary>Stages new slots for insertion (not yet committed).</summary>
    Task AddRangeAsync(IReadOnlyCollection<Slot> slots, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stages slots for permanent removal. A hard delete, unlike the soft delete used for
    /// user-authored records — see <see cref="Scheduling.SlotReconciliationPlan.ToDelete"/>.
    /// </summary>
    void RemoveRange(IReadOnlyCollection<Slot> slots);
}
