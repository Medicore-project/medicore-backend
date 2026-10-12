namespace MediCore.Billing.Application.DTOs;

public sealed record CreateServiceTariffRequest(
    string ServiceCode,
    string Description,
    decimal UnitPrice,
    string Currency,
    DateTime EffectiveFromUtc);

public sealed record UpdateServiceTariffRequest(
    string Description,
    decimal UnitPrice,
    string Currency,
    DateTime EffectiveFromUtc);

public sealed record ServiceTariffResponse(
    Guid TariffId,
    string ServiceCode,
    string Description,
    decimal UnitPrice,
    string Currency,
    DateTime EffectiveFromUtc,
    DateTime? EffectiveToUtc,
    bool IsActive);
