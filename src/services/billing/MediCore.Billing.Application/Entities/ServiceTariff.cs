namespace MediCore.Billing.Application.Entities;

public sealed class ServiceTariff
{
    public Guid TariffId { get; set; } = Guid.NewGuid();
    public string ServiceCode { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public string Currency { get; set; } = "LKR";
    public DateTime EffectiveFromUtc { get; set; }
    public DateTime? EffectiveToUtc { get; set; }
    public bool IsActive { get; set; } = true;
}
