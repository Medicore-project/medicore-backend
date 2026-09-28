namespace MediCore.Appointment.Application.Exceptions;

/// <summary>
/// Thrown when a save violates <c>ux_waitlist_entries_patient_active</c> — the patient already has
/// an active entry in that queue.
/// </summary>
/// <remarks>
/// Joining checks for an existing entry while holding the queue lock, so this only fires if an
/// entry was written without that lock. It is the backstop, not the check. The waitlist service
/// answers it as "already waiting".
/// </remarks>
public sealed class DuplicateWaitlistEntryException : Exception
{
    public DuplicateWaitlistEntryException(Exception? innerException = null)
        : base("This patient is already on the waitlist for that day.", innerException)
    {
    }
}
