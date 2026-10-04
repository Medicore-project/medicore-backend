using MediCore.Billing.Application.Entities;

namespace MediCore.Billing.Application.Interfaces;

public interface IServiceTariffRepository
{
    Task<ServiceTariff?> FindEffectiveAsync(
        string serviceCode,
        DateTime effectiveAtUtc,
        CancellationToken cancellationToken = default);
}
