using MediCore.Billing.Application.DTOs;

namespace MediCore.Billing.Application.Services;

public abstract record CreateServiceTariffResult;
public sealed record ServiceTariffCreatedResult(ServiceTariffResponse Tariff) : CreateServiceTariffResult;
public sealed record DuplicateServiceTariffCodeResult(string ServiceCode) : CreateServiceTariffResult;

public abstract record UpdateServiceTariffResult;
public sealed record ServiceTariffVersionCreatedResult(ServiceTariffResponse Tariff) : UpdateServiceTariffResult;
public sealed record ServiceTariffNotFoundResult : UpdateServiceTariffResult;
public sealed record ServiceTariffInactiveResult : UpdateServiceTariffResult;
public sealed record ServiceTariffEffectiveDateConflictResult(DateTime LatestEffectiveFromUtc) : UpdateServiceTariffResult;

public enum DeactivateServiceTariffResult
{
    Deactivated,
    NotFound
}

public interface IServiceTariffService
{
    Task<IReadOnlyList<ServiceTariffResponse>> GetAllAsync(
        bool includeInactive,
        CancellationToken cancellationToken = default);

    Task<ServiceTariffResponse?> GetByIdAsync(
        Guid tariffId,
        CancellationToken cancellationToken = default);

    Task<CreateServiceTariffResult> CreateAsync(
        CreateServiceTariffRequest request,
        CancellationToken cancellationToken = default);

    Task<UpdateServiceTariffResult> UpdatePriceAsync(
        Guid tariffId,
        UpdateServiceTariffRequest request,
        CancellationToken cancellationToken = default);

    Task<DeactivateServiceTariffResult> DeactivateAsync(
        Guid tariffId,
        CancellationToken cancellationToken = default);
}
