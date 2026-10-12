namespace MediCore.Billing.Application.Exceptions;

public sealed class DuplicateProcessedMessageException : Exception
{
    public DuplicateProcessedMessageException(Exception? innerException = null)
        : base("The integration event has already been processed.", innerException)
    {
    }
}
