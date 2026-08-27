namespace MediCore.Contracts.Events.Billing;

public sealed record InvoicePaidEvent : IntegrationEvent
{
    public override string EventType => "invoice.paid";

    public required Guid InvoiceId { get; init; }
    public required Guid PatientId { get; init; }
    public required decimal Amount { get; init; }
    public required string Method { get; init; }
}
