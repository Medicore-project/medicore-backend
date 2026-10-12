namespace MediCore.Contracts.Events.Billing;

public sealed record InvoicePaidEvent : IntegrationEvent
{
    public override string EventType => "invoice.paid";

    public required Guid InvoiceId { get; init; }
    public required Guid PatientId { get; init; }
    public required decimal Amount { get; init; }
    public required string Method { get; init; }
}

/// <summary>
/// A payment that leaves a balance still owing. Published instead of <see cref="InvoicePaidEvent"/>,
/// never alongside it: each payment produces exactly one of the two, so each gets one receipt.
/// </summary>
public sealed record InvoicePartiallyPaidEvent : IntegrationEvent
{
    public override string EventType => "invoice.partially-paid";

    public required Guid InvoiceId { get; init; }
    public required string InvoiceNumber { get; init; }
    public required Guid PatientId { get; init; }

    /// <summary>This payment alone, not the running total.</summary>
    public required decimal Amount { get; init; }
    public required string Method { get; init; }
    public required decimal TotalPaid { get; init; }
    public required decimal BalanceDue { get; init; }
    public required string Currency { get; init; }
}
