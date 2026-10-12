using MediCore.Billing.Application.Entities;
using MediCore.Billing.Application.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace MediCore.Billing.Infrastructure.Persistence.Repositories;

public sealed class PatientContactRepository : IPatientContactRepository
{
    private readonly BillingDbContext _dbContext;

    public PatientContactRepository(BillingDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<PatientContact?> GetAsync(Guid patientId, CancellationToken cancellationToken = default) =>
        _dbContext.PatientContacts.FirstOrDefaultAsync(contact => contact.PatientId == patientId, cancellationToken);

    public Task AddAsync(PatientContact contact, CancellationToken cancellationToken = default) =>
        _dbContext.PatientContacts.AddAsync(contact, cancellationToken).AsTask();
}
