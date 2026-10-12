using MediCore.Billing.Application.DTOs;

namespace MediCore.Billing.Application.Services;

public interface IInvoiceQueryService
{
    Task<InvoiceResponse?> GetByIdAsync(Guid invoiceId, CancellationToken cancellationToken = default);
    Task<InvoiceResponse?> GetByAppointmentIdAsync(Guid appointmentId, CancellationToken cancellationToken = default);
}
