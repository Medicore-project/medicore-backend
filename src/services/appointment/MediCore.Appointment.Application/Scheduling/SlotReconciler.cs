using MediCore.Appointment.Application.Entities;

namespace MediCore.Appointment.Application.Scheduling;

/// <inheritdoc cref="ISlotReconciler"/>
public sealed class SlotReconciler : ISlotReconciler
{
    /// <summary>
    /// Identity of a slot as the database sees it, matching the columns of
    /// <c>ux_slots_doctor_start</c>.
    /// </summary>
    private readonly record struct SlotKey(Guid DoctorId, DateTime StartUtc);

    /// <inheritdoc />
    public SlotReconciliationPlan Reconcile(
        IReadOnlyCollection<Slot> desired,
        IReadOnlyCollection<Slot> existing,
        DateTime nowUtc)
    {
        ArgumentNullException.ThrowIfNull(desired);
        ArgumentNullException.ThrowIfNull(existing);

        // ── Index what currently occupies each instant ───────────────────────
        // Flagged rows are deliberately left out. The partial unique index excludes them, so a
        // flagged slot does not reserve its instant: the doctor is once again free at that time and
        // a new bookable slot must be able to appear beside the flagged record of the old booking.
        var occupied = new Dictionary<SlotKey, Slot>();

        foreach (var slot in existing)
        {
            if (slot.IsDeleted || IsStatus(slot, SlotStatus.Flagged))
            {
                continue;
            }

            occupied.TryAdd(new SlotKey(slot.DoctorId, slot.StartUtc), slot);
        }

        // ── Everything the schedules want ────────────────────────────────────
        var wanted = new HashSet<SlotKey>();
        var toInsert = new List<Slot>();

        foreach (var slot in desired)
        {
            var key = new SlotKey(slot.DoctorId, slot.StartUtc);

            // Guards against two schedules of the same doctor implying the same instant. Overlap
            // detection should prevent it, but a duplicate here would hit the unique index at save
            // time, so it is collapsed rather than trusted.
            if (!wanted.Add(key))
            {
                continue;
            }

            // A slot in the past is never created: it could not be booked, and back-filling history
            // is not this operation's job.
            if (slot.StartUtc < nowUtc)
            {
                continue;
            }

            if (!occupied.ContainsKey(key))
            {
                toInsert.Add(slot);
            }
        }

        // ── Everything stored that the schedules no longer want ──────────────
        var toDelete = new List<Slot>();
        var toFlag = new List<Slot>();

        foreach (var slot in occupied.Values)
        {
            if (slot.StartUtc < nowUtc)
            {
                continue;
            }

            if (wanted.Contains(new SlotKey(slot.DoctorId, slot.StartUtc)))
            {
                continue;
            }

            if (IsStatus(slot, SlotStatus.Booked))
            {
                // AC2 — a booking outside the new schedule is preserved for a receptionist to
                // reschedule, never deleted.
                toFlag.Add(slot);
            }
            else
            {
                // Available, and Blocked too: a block is an annotation on a slot that ought to
                // exist, so once the schedule stops covering that time the block has nothing left
                // to describe.
                toDelete.Add(slot);
            }
        }

        if (toInsert.Count == 0 && toDelete.Count == 0 && toFlag.Count == 0)
        {
            return SlotReconciliationPlan.Empty;
        }

        // Dictionary iteration order is unspecified; sorting keeps the plan deterministic so that
        // logs and any downstream batching read the same way run to run.
        return new SlotReconciliationPlan(
            toInsert,
            Chronological(toDelete),
            Chronological(toFlag));
    }

    private static bool IsStatus(Slot slot, string status) =>
        string.Equals(slot.Status, status, StringComparison.Ordinal);

    private static IReadOnlyList<Slot> Chronological(List<Slot> slots)
    {
        slots.Sort((left, right) => left.StartUtc.CompareTo(right.StartUtc));
        return slots;
    }
}
