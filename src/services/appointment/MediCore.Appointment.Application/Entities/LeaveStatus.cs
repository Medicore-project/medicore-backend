namespace MediCore.Appointment.Application.Entities;

/// <summary>
/// Approval status constants for <see cref="DoctorLeave.Status"/>.
/// String constants rather than a C# enum, matching <see cref="SlotStatus"/>: the column stays
/// human-readable and adding a status later needs no enum migration.
/// </summary>
/// <remarks>
/// Leave is a request, not a unilateral act. A doctor submits it and an administrator decides,
/// so every leave record starts at <see cref="Pending"/> and only an explicit decision moves it on.
/// </remarks>
public static class LeaveStatus
{
    /// <summary>Submitted and awaiting an administrator's decision. The status every request starts in.</summary>
    public const string Pending = "Pending";

    /// <summary>Granted by an administrator. This is the only status that suppresses slot generation.</summary>
    public const string Approved = "Approved";

    /// <summary>Declined by an administrator. The doctor keeps working those days.</summary>
    public const string Rejected = "Rejected";

    /// <summary>Whether <paramref name="status"/> is one of the three known values.</summary>
    public static bool IsValid(string? status) =>
        status is Pending or Approved or Rejected;

    /// <summary>
    /// Whether leave in this status removes the doctor's slots.
    /// </summary>
    /// <remarks>
    /// Only <see cref="Approved"/> qualifies, and that is the whole point of the workflow: if
    /// <see cref="Pending"/> suppressed slots, a doctor could clear their own calendar simply by
    /// asking, and the approval step would decide nothing. Callers should prefer this method over
    /// comparing to <see cref="Approved"/> directly so the rule lives in exactly one place.
    /// </remarks>
    public static bool SuppressesSlots(string? status) =>
        string.Equals(status, Approved, StringComparison.Ordinal);

    /// <summary>
    /// Whether an administrator may move a request from <paramref name="from"/> to
    /// <paramref name="to"/>.
    /// </summary>
    /// <remarks>
    /// A decision may always be corrected — an approval can be revoked and a rejection reversed —
    /// but nothing returns to <see cref="Pending"/>, because a decision that has been made cannot
    /// be un-made, and a no-op restatement of the current status is not a decision either.
    /// <para>
    /// Note that revoking an approval changes which slots should exist, so callers must regenerate
    /// the affected dates (SCRUM-32 Step 5) rather than only writing the new status.
    /// </para>
    /// </remarks>
    public static bool CanTransitionTo(string? from, string? to) =>
        IsValid(from)
        && to is Approved or Rejected
        && !string.Equals(from, to, StringComparison.Ordinal);
}
