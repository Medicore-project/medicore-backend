namespace MediCore.Billing.Application.Exceptions;

public sealed class ConcurrentPaymentException : Exception
{
    public ConcurrentPaymentException(Exception? innerException = null)
        : base("The invoice balance changed while the payment was being recorded.", innerException)
    {
    }
}
