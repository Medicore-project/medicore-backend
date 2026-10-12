namespace MediCore.Billing.Application.Exceptions;

public sealed class NotificationDeliveryException : Exception
{
    public NotificationDeliveryException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
