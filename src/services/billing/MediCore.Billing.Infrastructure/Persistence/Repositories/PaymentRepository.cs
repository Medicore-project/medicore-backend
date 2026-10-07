using MediCore.Billing.Application.Entities;
using MediCore.Billing.Application.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace MediCore.Billing.Infrastructure.Persistence.Repositories;

public sealed class PaymentRepository : IPaymentRepository
{
    private readonly BillingDbContext _dbContext;

    public PaymentRepository(BillingDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<Invoice?> GetInvoiceForPaymentAsync(
        Guid invoiceId,
        CancellationToken cancellationToken = default) =>
        _dbContext.Invoices
            .Include(invoice => invoice.Lines)
            .Include(invoice => invoice.Payments)
            .SingleOrDefaultAsync(invoice => invoice.InvoiceId == invoiceId, cancellationToken);

    public Task AddAsync(Payment payment, CancellationToken cancellationToken = default) =>
        _dbContext.Payments.AddAsync(payment, cancellationToken).AsTask();
}
