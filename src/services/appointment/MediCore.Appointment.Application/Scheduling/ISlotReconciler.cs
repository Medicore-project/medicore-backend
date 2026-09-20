using MediCore.Appointment.Application.Entities;

namespace MediCore.Appointment.Application.Scheduling;

/// <summary>
/// The changes needed to bring a doctor's stored slots in line with what their schedules imply.
/// Describes intent only — nothing is mutated until a caller applies it.
/// </summary>
/// <param name="ToInsert">New slots to add. Freshly built, not yet tracked.</param>
/// <param name="ToDelete">
/// Existing slots to remove outright. Hard delete rather than soft: slots are generated data, and a
/// soft-deleted row would accumulate forever every time a schedule is edited.
/// </param>
/// <param name="ToFlag">
/// Existing <see cref="SlotStatus.Booked"/> slots that no longer fit the schedule. Retained rather
/// than deleted so a booking is never silently lost (SCRUM-32 AC2).
/// </param>
public sealed record SlotReconciliationPlan(
    IReadOnlyList<Slot> ToInsert,
    IReadOnlyList<Slot> ToDelete,
    IReadOnlyList<Slot> ToFlag)
{
    /// <summary>A plan that changes nothing.</summary>
    public static SlotReconciliationPlan Empty { get; } = new([], [], []);

    /// <summary>Whether applying this plan would write anything.</summary>
    public bool HasChanges => ToInsert.Count > 0 || ToDelete.Count > 0 || ToFlag.Count > 0;
}

/// <summary>
/// Works out how stored slots differ from the slots a doctor's schedules currently imply.
/// </summary>
public interface ISlotReconciler
{
    /// <summary>
    /// Compares the <paramref name="desired"/> slots against the <paramref name="existing"/> ones
    /// and returns the changes that would reconcile them.
    /// </summary>
    /// <remarks>
    /// Pure: no database access, no clock reads, and neither input collection is mutated.
    /// <para>
    /// Four rules govern the result:
    /// </para>
    /// <list type="bullet">
    /// <item>
    /// The past is frozen. Nothing starting before <paramref name="nowUtc"/> is inserted, deleted
    /// or flagged, so history is never rewritten.
    /// </item>
    /// <item>
    /// A slot that is no longer covered is deleted when it is free and flagged when it is booked —
    /// the AC2 rule that a booking survives a schedule change.
    /// </item>
    /// <item>
    /// <see cref="SlotStatus.Flagged"/> slots are inert: never revived, never deleted, and never
    /// counted as occupying their time. A fresh bookable slot may therefore be inserted alongside
    /// one, which is exactly what the partial unique index <c>ux_slots_doctor_start</c> permits.
    /// </item>
    /// <item>
    /// Slots that already exist and are still wanted are left untouched, so reconciling twice in a
    /// row produces no writes the second time.
    /// </item>
    /// </list>
    /// </remarks>
    /// <param name="desired">Slots the doctor's active schedules imply over the window.</param>
    /// <param name="existing">Slots currently stored for that doctor over the same window.</param>
    /// <param name="nowUtc">The boundary between frozen history and the mutable future.</param>
    SlotReconciliationPlan Reconcile(
        IReadOnlyCollection<Slot> desired,
        IReadOnlyCollection<Slot> existing,
        DateTime nowUtc);
}
