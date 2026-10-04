using MediCore.Billing.Application.Entities;
using MediCore.Billing.Application.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace MediCore.Billing.Infrastructure.Persistence.Repositories;

public sealed class ServiceTariffRepository : IServiceTariffRepository
{
    private readonly BillingDbContext _dbContext;

    public ServiceTariffRepository(BillingDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<ServiceTariff?> FindEffectiveAsync(
        string serviceCode,
        DateTime effectiveAtUtc,
        CancellationToken cancellationToken = default) =>
        _dbContext.ServiceTariffs.AsNoTracking()
            .Where(tariff => tariff.ServiceCode == serviceCode
                && tariff.IsActive
                && tariff.EffectiveFromUtc <= effectiveAtUtc
                && (tariff.EffectiveToUtc == null || tariff.EffectiveToUtc > effectiveAtUtc))
            .OrderByDescending(tariff => tariff.EffectiveFromUtc)
            .FirstOrDefaultAsync(cancellationToken);
}
