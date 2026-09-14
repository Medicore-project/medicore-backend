namespace MediCore.Patient.Application.Exceptions;

public sealed class DuplicateProcessedMessageException : Exception
{
    public DuplicateProcessedMessageException(Exception? innerException = null)
        : base("The integration message has already been processed.", innerException)
    {
    }
}
