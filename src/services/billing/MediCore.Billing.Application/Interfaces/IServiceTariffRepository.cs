using MediCore.Billing.Application.Entities;

namespace MediCore.Billing.Application.Interfaces;

public interface IServiceTariffRepository
{
    Task<IReadOnlyList<ServiceTariff>> GetAllAsync(
        bool includeInactive,
        CancellationToken cancellationToken = default);

    Task<ServiceTariff?> GetByIdAsync(
        Guid tariffId,
        CancellationToken cancellationToken = default);

    Task<bool> CodeExistsAsync(
        string serviceCode,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ServiceTariff>> GetVersionsAsync(
        string serviceCode,
        CancellationToken cancellationToken = default);

    Task<ServiceTariff?> FindEffectiveAsync(
        string serviceCode,
        DateTime effectiveAtUtc,
        CancellationToken cancellationToken = default);

    Task AddAsync(
        ServiceTariff tariff,
        CancellationToken cancellationToken = default);
}
