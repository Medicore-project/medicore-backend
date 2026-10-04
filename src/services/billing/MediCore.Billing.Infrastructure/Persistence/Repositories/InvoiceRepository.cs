using MediCore.Billing.Application.Entities;
using MediCore.Billing.Application.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace MediCore.Billing.Infrastructure.Persistence.Repositories;

public sealed class InvoiceRepository : IInvoiceRepository
{
    private readonly BillingDbContext _dbContext;

    public InvoiceRepository(BillingDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<Invoice?> GetTrackedByAppointmentIdAsync(
        Guid appointmentId,
        CancellationToken cancellationToken = default) =>
        _dbContext.Invoices.SingleOrDefaultAsync(
            invoice => invoice.AppointmentId == appointmentId, cancellationToken);

    public Task<Invoice?> GetByIdAsync(Guid invoiceId, CancellationToken cancellationToken = default) =>
        _dbContext.Invoices.AsNoTracking().Include(invoice => invoice.Lines)
            .SingleOrDefaultAsync(invoice => invoice.InvoiceId == invoiceId, cancellationToken);

    public Task<Invoice?> GetByAppointmentIdAsync(
        Guid appointmentId,
        CancellationToken cancellationToken = default) =>
        _dbContext.Invoices.AsNoTracking().Include(invoice => invoice.Lines)
            .SingleOrDefaultAsync(invoice => invoice.AppointmentId == appointmentId, cancellationToken);

    public Task AddAsync(Invoice invoice, CancellationToken cancellationToken = default) =>
        _dbContext.Invoices.AddAsync(invoice, cancellationToken).AsTask();
}
