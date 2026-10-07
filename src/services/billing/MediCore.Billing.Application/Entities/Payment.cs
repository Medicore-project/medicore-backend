namespace MediCore.Billing.Application.Entities;

public sealed class Payment
{
    public Guid PaymentId { get; set; } = Guid.NewGuid();
    public Guid InvoiceId { get; set; }
    public decimal Amount { get; set; }
    public string Method { get; set; } = string.Empty;
    public DateTime RecordedAtUtc { get; set; }
    public string RecordedBy { get; set; } = string.Empty;
    public Invoice Invoice { get; set; } = null!;
}
