namespace MediCore.Billing.Application.Entities;

public sealed class InvoiceLine
{
    public Guid InvoiceLineId { get; set; } = Guid.NewGuid();
    public Guid InvoiceId { get; set; }
    public Guid? TariffId { get; set; }
    public string ServiceCode { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int Quantity { get; set; } = 1;
    public decimal? UnitPrice { get; set; }
    public decimal LineTotal { get; set; }
    public Invoice Invoice { get; set; } = null!;
}
