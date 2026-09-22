namespace MediCore.Appointment.Application.Exceptions;

/// <summary>
/// Thrown when a save violates <c>ux_slots_doctor_start</c> — a bookable slot already exists for
/// that doctor at that instant.
/// </summary>
/// <remarks>
/// Reconciliation checks what exists before inserting, so this means two reconciliations of the
/// same doctor overlapped. The constraint is the backstop that keeps the race from producing a
/// duplicate bookable slot. Controllers map it to 409.
/// </remarks>
public sealed class DuplicateSlotException : Exception
{
    public DuplicateSlotException(Exception? innerException = null)
        : base(
            "A slot already exists for this doctor at one of the requested times. "
            + "The schedule was most likely regenerated concurrently — retry the operation.",
            innerException)
    {
    }
}
