namespace MediCore.Billing.Application.Exceptions;

public sealed class DuplicateNotificationLogException : Exception
{
    public DuplicateNotificationLogException(Exception innerException)
        : base("This event has already created the same notification.", innerException)
    {
    }
}
