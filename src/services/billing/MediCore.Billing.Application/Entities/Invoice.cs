namespace MediCore.Billing.Application.Entities;

public sealed class Invoice
{
    public Guid InvoiceId { get; set; } = Guid.NewGuid();
    public string InvoiceNumber { get; set; } = string.Empty;
    public Guid AppointmentId { get; set; }
    public Guid PatientId { get; set; }
    public string ServiceCode { get; set; } = string.Empty;
    public string Status { get; set; } = InvoiceStatus.Draft;
    public string Currency { get; set; } = "LKR";
    public decimal Subtotal { get; set; }
    public decimal Total { get; set; }
    public decimal AmountPaid { get; set; }
    public bool RequiresManualPricing { get; set; }
    public string? PricingIssue { get; set; }
    public DateTime IssuedAtUtc { get; set; }
    public DateTime? FinalizedAtUtc { get; set; }
    public DateTime? VoidedAtUtc { get; set; }
    public string? VoidReason { get; set; }
    public DateTime? PaidAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }
    public int Version { get; set; }
    public ICollection<InvoiceLine> Lines { get; set; } = new List<InvoiceLine>();
    public ICollection<Payment> Payments { get; set; } = new List<Payment>();
}
