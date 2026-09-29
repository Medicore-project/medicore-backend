namespace MediCore.Appointment.Application.Exceptions;

/// <summary>
/// Thrown when a save violates <c>ux_appointments_slot</c> — an active appointment already exists
/// for that slot.
/// </summary>
/// <remarks>
/// Booking checks the slot is <c>Available</c> before writing, so this means two bookings of the
/// same slot overlapped. The check can never be enough on its own: booking mutates the slot rather
/// than inserting it, so both racers read <c>Available</c> and both write <c>Booked</c>
/// successfully. The unique index on the appointment row is the backstop that lets exactly one of
/// them win. The booking service maps it to 409.
/// </remarks>
public sealed class SlotAlreadyBookedException : Exception
{
    public SlotAlreadyBookedException(Exception? innerException = null)
        : base(
            "This slot was booked by someone else a moment ago. Please choose another slot.",
            innerException)
    {
    }
}
