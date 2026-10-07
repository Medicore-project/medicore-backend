using MediCore.Billing.Application.Entities;

namespace MediCore.Billing.Application.Interfaces;

public interface IPaymentRepository
{
    Task<Invoice?> GetInvoiceForPaymentAsync(
        Guid invoiceId,
        CancellationToken cancellationToken = default);

    Task AddAsync(Payment payment, CancellationToken cancellationToken = default);
}
