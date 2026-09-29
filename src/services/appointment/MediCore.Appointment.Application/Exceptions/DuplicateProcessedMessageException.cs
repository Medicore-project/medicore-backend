namespace MediCore.Appointment.Application.Exceptions;

/// <summary>
/// Thrown when a save violates <c>pk_processed_messages</c> — the same message was handled
/// concurrently and the other attempt committed first. The consumer treats it as a duplicate.
/// </summary>
public sealed class DuplicateProcessedMessageException : Exception
{
    public DuplicateProcessedMessageException(Exception? innerException = null)
        : base("The integration message has already been processed.", innerException)
    {
    }
}
