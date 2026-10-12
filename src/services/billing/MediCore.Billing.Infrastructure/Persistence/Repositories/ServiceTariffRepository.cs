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

    public async Task<IReadOnlyList<ServiceTariff>> GetAllAsync(
        bool includeInactive,
        CancellationToken cancellationToken = default)
    {
        var query = _dbContext.ServiceTariffs.AsNoTracking();
        if (!includeInactive)
        {
            query = query.Where(tariff => tariff.IsActive);
        }

        return await query
            .OrderBy(tariff => tariff.ServiceCode)
            .ThenByDescending(tariff => tariff.EffectiveFromUtc)
            .ToListAsync(cancellationToken);
    }

    public Task<ServiceTariff?> GetByIdAsync(
        Guid tariffId,
        CancellationToken cancellationToken = default) =>
        _dbContext.ServiceTariffs
            .FirstOrDefaultAsync(tariff => tariff.TariffId == tariffId, cancellationToken);

    public Task<bool> CodeExistsAsync(
        string serviceCode,
        CancellationToken cancellationToken = default) =>
        _dbContext.ServiceTariffs.AnyAsync(
            tariff => tariff.ServiceCode == serviceCode,
            cancellationToken);

    public async Task<IReadOnlyList<ServiceTariff>> GetVersionsAsync(
        string serviceCode,
        CancellationToken cancellationToken = default) =>
        await _dbContext.ServiceTariffs
            .Where(tariff => tariff.ServiceCode == serviceCode)
            .OrderBy(tariff => tariff.EffectiveFromUtc)
            .ToListAsync(cancellationToken);

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

    public Task AddAsync(
        ServiceTariff tariff,
        CancellationToken cancellationToken = default) =>
        _dbContext.ServiceTariffs.AddAsync(tariff, cancellationToken).AsTask();
}
