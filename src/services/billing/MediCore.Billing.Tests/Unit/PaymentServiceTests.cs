using System.Text.Json;
using MediCore.Billing.Application.DTOs;
using MediCore.Billing.Application.Entities;
using MediCore.Billing.Application.Exceptions;
using MediCore.Billing.Application.Interfaces;
using MediCore.Billing.Application.Services;
using MediCore.Billing.Application.Validators;
using MediCore.Contracts.Events.Billing;

namespace MediCore.Billing.Tests.Unit;

public sealed class PaymentServiceTests
{
    private static readonly DateTime NowUtc = new(2026, 10, 6, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Partial_payment_reduces_balance_and_keeps_invoice_payable()
    {
        var fixture = new Fixture(Invoice());

        var result = await fixture.Service.RecordAsync(
            fixture.Invoice.InvoiceId,
            new RecordPaymentRequest(400m, " cash "),
            "reception@medicore.lk",
            "corr-payment");

        var recorded = Assert.IsType<PaymentRecordedResult>(result);
        Assert.Equal(InvoiceStatus.Payable, recorded.Invoice.Status);
        Assert.Equal(400m, recorded.Invoice.AmountPaid);
        Assert.Equal(600m, recorded.Invoice.BalanceDue);
        var payment = Assert.Single(recorded.Invoice.Payments);
        Assert.Equal(PaymentMethods.Cash, payment.Method);
        Assert.Equal("reception@medicore.lk", payment.RecordedBy);
        Assert.Empty(fixture.Outbox.Added);
        Assert.Equal(1, fixture.UnitOfWork.SaveCount);
    }

    [Fact]
    public async Task Final_payment_marks_invoice_paid_and_writes_invoice_paid_outbox_event()
    {
        var invoice = Invoice();
        invoice.AmountPaid = 400m;
        invoice.Payments.Add(new Payment
        {
            InvoiceId = invoice.InvoiceId,
            Amount = 400m,
            Method = PaymentMethods.Cash,
            RecordedAtUtc = NowUtc.AddHours(-1),
            RecordedBy = "first@medicore.lk"
        });
        var fixture = new Fixture(invoice);

        var result = await fixture.Service.RecordAsync(
            invoice.InvoiceId,
            new RecordPaymentRequest(600m, "Card"),
            "reception@medicore.lk",
            "corr-paid");

        var recorded = Assert.IsType<PaymentRecordedResult>(result);
        Assert.Equal(InvoiceStatus.Paid, recorded.Invoice.Status);
        Assert.Equal(1000m, recorded.Invoice.AmountPaid);
        Assert.Equal(0m, recorded.Invoice.BalanceDue);
        Assert.Equal(NowUtc, recorded.Invoice.PaidAtUtc);
        Assert.Equal(2, recorded.Invoice.Payments.Count);

        var outbox = Assert.Single(fixture.Outbox.Added);
        Assert.Equal("billing-events", outbox.Topic);
        Assert.Equal(invoice.InvoiceId.ToString(), outbox.EventKey);
        Assert.Equal("invoice.paid", outbox.EventType);
        Assert.Equal("corr-paid", outbox.CorrelationId);
        var paidEvent = JsonSerializer.Deserialize<InvoicePaidEvent>(
            outbox.Payload,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.NotNull(paidEvent);
        Assert.Equal(invoice.InvoiceId, paidEvent.InvoiceId);
        Assert.Equal(invoice.PatientId, paidEvent.PatientId);
        Assert.Equal(1000m, paidEvent.Amount);
        Assert.Equal(PaymentMethods.Card, paidEvent.Method);
        Assert.Equal(1, fixture.UnitOfWork.SaveCount);
    }

    [Fact]
    public async Task Overpayment_is_rejected_without_persisting_any_change()
    {
        var fixture = new Fixture(Invoice());

        var result = await fixture.Service.RecordAsync(
            fixture.Invoice.InvoiceId,
            new RecordPaymentRequest(1000.01m, PaymentMethods.Insurance),
            "reception@medicore.lk",
            "corr-overpay");

        var overpayment = Assert.IsType<PaymentOverpaymentResult>(result);
        Assert.Equal(1000m, overpayment.BalanceDue);
        Assert.Empty(fixture.Payments.Added);
        Assert.Empty(fixture.Outbox.Added);
        Assert.Equal(0m, fixture.Invoice.AmountPaid);
        Assert.Equal(0, fixture.UnitOfWork.SaveCount);
    }

    [Theory]
    [InlineData(InvoiceStatus.Draft)]
    [InlineData(InvoiceStatus.Paid)]
    [InlineData(InvoiceStatus.Void)]
    public async Task Payment_is_rejected_when_invoice_is_not_payable(string status)
    {
        var invoice = Invoice();
        invoice.Status = status;
        var fixture = new Fixture(invoice);

        var result = await fixture.Service.RecordAsync(
            invoice.InvoiceId,
            new RecordPaymentRequest(100m, PaymentMethods.Cash),
            "reception@medicore.lk",
            "corr-state");

        var rejected = Assert.IsType<PaymentInvoiceNotPayableResult>(result);
        Assert.Equal(status, rejected.Status);
        Assert.Empty(fixture.Payments.Added);
        Assert.Equal(0, fixture.UnitOfWork.SaveCount);
    }

    [Fact]
    public async Task Concurrent_balance_change_returns_conflict_result()
    {
        var fixture = new Fixture(Invoice());
        fixture.UnitOfWork.ThrowConcurrency = true;

        var result = await fixture.Service.RecordAsync(
            fixture.Invoice.InvoiceId,
            new RecordPaymentRequest(100m, PaymentMethods.Cash),
            "reception@medicore.lk",
            "corr-race");

        Assert.IsType<PaymentConcurrentUpdateResult>(result);
    }

    [Theory]
    [InlineData(0, "Cash")]
    [InlineData(-1, "Cash")]
    [InlineData(100, "Cheque")]
    public async Task Validator_rejects_invalid_payment_requests(decimal amount, string method)
    {
        var result = await new RecordPaymentRequestValidator()
            .ValidateAsync(new RecordPaymentRequest(amount, method));

        Assert.False(result.IsValid);
    }

    private static Invoice Invoice() => new()
    {
        InvoiceId = Guid.NewGuid(),
        AppointmentId = Guid.NewGuid(),
        PatientId = Guid.NewGuid(),
        InvoiceNumber = "INV-20261006-TEST0001",
        ServiceCode = "GEN-CONSULT",
        Status = InvoiceStatus.Payable,
        Currency = "LKR",
        Subtotal = 1000m,
        Total = 1000m,
        IssuedAtUtc = NowUtc.AddDays(-1),
        FinalizedAtUtc = NowUtc.AddHours(-2),
        CreatedAtUtc = NowUtc.AddDays(-1)
    };

    private sealed class Fixture
    {
        public Fixture(Invoice invoice)
        {
            Invoice = invoice;
            Payments.Invoice = invoice;
            Service = new PaymentService(
                Payments,
                Outbox,
                UnitOfWork,
                new FixedTimeProvider(NowUtc));
        }

        public Invoice Invoice { get; }
        public FakePaymentRepository Payments { get; } = new();
        public FakeOutboxRepository Outbox { get; } = new();
        public FakeUnitOfWork UnitOfWork { get; } = new();
        public PaymentService Service { get; }
    }

    private sealed class FixedTimeProvider(DateTime utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utcNow);
    }

    private sealed class FakePaymentRepository : IPaymentRepository
    {
        public Invoice? Invoice { get; set; }
        public List<Payment> Added { get; } = [];

        public Task<Invoice?> GetInvoiceForPaymentAsync(
            Guid invoiceId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Invoice?.InvoiceId == invoiceId ? Invoice : null);

        public Task AddAsync(Payment payment, CancellationToken cancellationToken = default)
        {
            Added.Add(payment);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeOutboxRepository : IOutboxMessageRepository
    {
        public List<OutboxMessage> Added { get; } = [];

        public Task AddAsync(OutboxMessage message, CancellationToken cancellationToken = default)
        {
            Added.Add(message);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<OutboxMessage>> GetUnprocessedBatchAsync(
            int batchSize,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<OutboxMessage>>([]);

        public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class FakeUnitOfWork : IUnitOfWork
    {
        public int SaveCount { get; private set; }
        public bool ThrowConcurrency { get; set; }

        public Task SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            SaveCount++;
            if (ThrowConcurrency)
            {
                throw new ConcurrentPaymentException();
            }

            return Task.CompletedTask;
        }
    }
}
