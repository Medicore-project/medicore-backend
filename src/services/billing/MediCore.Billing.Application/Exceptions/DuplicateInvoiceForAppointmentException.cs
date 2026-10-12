namespace MediCore.Billing.Application.Exceptions;

public sealed class DuplicateInvoiceForAppointmentException : Exception
{
    public DuplicateInvoiceForAppointmentException(Exception? innerException = null)
        : base("An invoice already exists for this appointment.", innerException)
    {
    }
}
