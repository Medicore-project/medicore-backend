using MediCore.Billing.Application.DTOs;
using MediCore.Billing.Application.Entities;
using MediCore.Billing.Application.Interfaces;

namespace MediCore.Billing.Application.Services;

public sealed class ServiceTariffService : IServiceTariffService
{
    private readonly IServiceTariffRepository _repository;
    private readonly IUnitOfWork _unitOfWork;

    public ServiceTariffService(IServiceTariffRepository repository, IUnitOfWork unitOfWork)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
    }

    public async Task<IReadOnlyList<ServiceTariffResponse>> GetAllAsync(
        bool includeInactive,
        CancellationToken cancellationToken = default) =>
        (await _repository.GetAllAsync(includeInactive, cancellationToken))
            .Select(Map)
            .ToArray();

    public async Task<ServiceTariffResponse?> GetByIdAsync(
        Guid tariffId,
        CancellationToken cancellationToken = default)
    {
        var tariff = await _repository.GetByIdAsync(tariffId, cancellationToken);
        return tariff is null ? null : Map(tariff);
    }

    public async Task<CreateServiceTariffResult> CreateAsync(
        CreateServiceTariffRequest request,
        CancellationToken cancellationToken = default)
    {
        var serviceCode = NormalizeCode(request.ServiceCode);
        if (await _repository.CodeExistsAsync(serviceCode, cancellationToken))
        {
            return new DuplicateServiceTariffCodeResult(serviceCode);
        }

        var tariff = new ServiceTariff
        {
            ServiceCode = serviceCode,
            Description = request.Description.Trim(),
            UnitPrice = request.UnitPrice,
            Currency = request.Currency.Trim().ToUpperInvariant(),
            EffectiveFromUtc = AsUtc(request.EffectiveFromUtc),
            IsActive = true
        };

        await _repository.AddAsync(tariff, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return new ServiceTariffCreatedResult(Map(tariff));
    }

    public async Task<UpdateServiceTariffResult> UpdatePriceAsync(
        Guid tariffId,
        UpdateServiceTariffRequest request,
        CancellationToken cancellationToken = default)
    {
        var selected = await _repository.GetByIdAsync(tariffId, cancellationToken);
        if (selected is null)
        {
            return new ServiceTariffNotFoundResult();
        }

        var versions = await _repository.GetVersionsAsync(selected.ServiceCode, cancellationToken);
        if (versions.Count == 0 || versions.All(version => !version.IsActive))
        {
            return new ServiceTariffInactiveResult();
        }

        var latest = versions.MaxBy(version => version.EffectiveFromUtc)!;
        var effectiveFromUtc = AsUtc(request.EffectiveFromUtc);
        if (effectiveFromUtc <= latest.EffectiveFromUtc)
        {
            return new ServiceTariffEffectiveDateConflictResult(latest.EffectiveFromUtc);
        }

        latest.EffectiveToUtc = effectiveFromUtc;
        var replacement = new ServiceTariff
        {
            ServiceCode = selected.ServiceCode,
            Description = request.Description.Trim(),
            UnitPrice = request.UnitPrice,
            Currency = request.Currency.Trim().ToUpperInvariant(),
            EffectiveFromUtc = effectiveFromUtc,
            IsActive = true
        };

        await _repository.AddAsync(replacement, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return new ServiceTariffVersionCreatedResult(Map(replacement));
    }

    public async Task<DeactivateServiceTariffResult> DeactivateAsync(
        Guid tariffId,
        CancellationToken cancellationToken = default)
    {
        var selected = await _repository.GetByIdAsync(tariffId, cancellationToken);
        if (selected is null)
        {
            return DeactivateServiceTariffResult.NotFound;
        }

        var versions = await _repository.GetVersionsAsync(selected.ServiceCode, cancellationToken);
        foreach (var version in versions)
        {
            version.IsActive = false;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return DeactivateServiceTariffResult.Deactivated;
    }

    private static string NormalizeCode(string serviceCode) =>
        serviceCode.Trim().ToUpperInvariant();

    private static DateTime AsUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };

    private static ServiceTariffResponse Map(ServiceTariff tariff) => new(
        tariff.TariffId,
        tariff.ServiceCode,
        tariff.Description,
        tariff.UnitPrice,
        tariff.Currency,
        tariff.EffectiveFromUtc,
        tariff.EffectiveToUtc,
        tariff.IsActive);
}
