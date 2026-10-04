using MediCore.Billing.Application.Entities;
using MediCore.Billing.Application.Interfaces;
using MediCore.Billing.Application.Services;
using MediCore.Contracts.Events.Appointment;
using Microsoft.Extensions.Logging.Abstractions;

namespace MediCore.Billing.Tests.Unit;

public sealed class AppointmentBillingHandlerTests
{
    private static readonly DateTime NowUtc = new(2026, 10, 4, 6, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Booked_creates_priced_draft_invoice_and_processed_marker()
    {
        var fixture = new Fixture();
        fixture.Tariffs.Result = new ServiceTariff
        {
            TariffId = Guid.NewGuid(),
            ServiceCode = "GEN-CONSULT",
            Description = "General consultation",
            UnitPrice = 2500m,
            Currency = "LKR",
            EffectiveFromUtc = NowUtc.AddYears(-1)
        };

        var result = await fixture.Handler.HandleBookedAsync(Booked());

        Assert.Equal(AppointmentBillingResult.Processed, result);
        var invoice = Assert.Single(fixture.Invoices.Added);
        Assert.Equal(InvoiceStatus.Draft, invoice.Status);
        Assert.Equal(2500m, invoice.Subtotal);
        Assert.Equal(2500m, invoice.Total);
        Assert.False(invoice.RequiresManualPricing);
        Assert.Null(invoice.PricingIssue);
        var line = Assert.Single(invoice.Lines);
        Assert.Equal(2500m, line.UnitPrice);
        Assert.Equal(fixture.Tariffs.Result.TariffId, line.TariffId);
        Assert.Equal("Processed", Assert.Single(fixture.Processed.Added).Outcome);
        Assert.Equal(1, fixture.UnitOfWork.SaveCount);
    }

    [Fact]
    public async Task Booked_without_tariff_persists_manual_pricing_invoice_instead_of_failing()
    {
        var fixture = new Fixture();

        var result = await fixture.Handler.HandleBookedAsync(Booked(serviceCode: "UNPRICED"));

        Assert.Equal(AppointmentBillingResult.MissingTariff, result);
        var invoice = Assert.Single(fixture.Invoices.Added);
        Assert.True(invoice.RequiresManualPricing);
        Assert.Equal(0m, invoice.Total);
        Assert.Contains("UNPRICED", invoice.PricingIssue);
        Assert.Null(Assert.Single(invoice.Lines).UnitPrice);
        Assert.Equal("MissingTariff", Assert.Single(fixture.Processed.Added).Outcome);
        Assert.Equal(1, fixture.UnitOfWork.SaveCount);
    }

    [Fact]
    public async Task Duplicate_message_is_checked_before_invoice_or_tariff_access()
    {
        var fixture = new Fixture();
        fixture.Processed.Exists = true;

        var result = await fixture.Handler.HandleBookedAsync(Booked());

        Assert.Equal(AppointmentBillingResult.Duplicate, result);
        Assert.Equal(1, fixture.Processed.ExistsCalls);
        Assert.Equal(0, fixture.Invoices.AppointmentLookupCalls);
        Assert.Equal(0, fixture.Tariffs.LookupCalls);
        Assert.Equal(0, fixture.UnitOfWork.SaveCount);
    }

    [Fact]
    public async Task Different_message_for_existing_appointment_records_semantic_duplicate()
    {
        var fixture = new Fixture();
        fixture.Invoices.Stored = Invoice();

        var result = await fixture.Handler.HandleBookedAsync(Booked());

        Assert.Equal(AppointmentBillingResult.InvoiceAlreadyExists, result);
        Assert.Empty(fixture.Invoices.Added);
        Assert.Equal("InvoiceAlreadyExists", Assert.Single(fixture.Processed.Added).Outcome);
        Assert.Equal(1, fixture.UnitOfWork.SaveCount);
    }

    [Fact]
    public async Task Completed_transitions_draft_invoice_to_payable()
    {
        var fixture = new Fixture();
        fixture.Invoices.Stored = Invoice();

        var result = await fixture.Handler.HandleCompletedAsync(Completed());

        Assert.Equal(AppointmentBillingResult.Processed, result);
        Assert.Equal(InvoiceStatus.Payable, fixture.Invoices.Stored.Status);
        Assert.Equal(NowUtc.AddMinutes(-1), fixture.Invoices.Stored.FinalizedAtUtc);
        Assert.Equal("Processed", Assert.Single(fixture.Processed.Added).Outcome);
        Assert.Equal(1, fixture.UnitOfWork.SaveCount);
    }

    [Fact]
    public async Task Completed_without_invoice_is_recorded_and_does_not_poison_partition()
    {
        var fixture = new Fixture();

        var result = await fixture.Handler.HandleCompletedAsync(Completed());

        Assert.Equal(AppointmentBillingResult.InvoiceNotFound, result);
        Assert.Equal("InvoiceNotFound", Assert.Single(fixture.Processed.Added).Outcome);
        Assert.Equal(1, fixture.UnitOfWork.SaveCount);
    }

    [Fact]
    public async Task Completed_duplicate_is_checked_before_invoice_access()
    {
        var fixture = new Fixture();
        fixture.Processed.Exists = true;

        var result = await fixture.Handler.HandleCompletedAsync(Completed());

        Assert.Equal(AppointmentBillingResult.Duplicate, result);
        Assert.Equal(0, fixture.Invoices.AppointmentLookupCalls);
        Assert.Equal(0, fixture.UnitOfWork.SaveCount);
    }

    private static AppointmentBookedEvent Booked(string serviceCode = "GEN-CONSULT") => new()
    {
        MessageId = Guid.NewGuid(),
        AppointmentId = AppointmentId,
        PatientId = Guid.NewGuid(),
        DoctorId = Guid.NewGuid(),
        SlotStart = NowUtc.AddDays(1),
        ServiceCode = serviceCode,
        OccurredAtUtc = NowUtc.AddMinutes(-5),
        CorrelationId = "corr-42"
    };

    private static AppointmentCompletedEvent Completed() => new()
    {
        MessageId = Guid.NewGuid(),
        AppointmentId = AppointmentId,
        PatientId = Guid.NewGuid(),
        Notes = "Consultation completed.",
        OccurredAtUtc = NowUtc.AddMinutes(-1),
        CorrelationId = "corr-42"
    };

    private static readonly Guid AppointmentId = Guid.Parse("de39865f-795c-4b52-823d-7df4e8c4f842");

    private static Invoice Invoice() => new()
    {
        AppointmentId = AppointmentId,
        PatientId = Guid.NewGuid(),
        InvoiceNumber = "INV-20261004-TEST0001",
        ServiceCode = "GEN-CONSULT",
        Status = InvoiceStatus.Draft,
        IssuedAtUtc = NowUtc.AddMinutes(-5),
        CreatedAtUtc = NowUtc.AddMinutes(-5)
    };

    private sealed class Fixture
    {
        public Fixture()
        {
            Handler = new AppointmentBillingHandler(
                Invoices,
                Tariffs,
                Processed,
                UnitOfWork,
                new FixedTimeProvider(NowUtc),
                NullLogger<AppointmentBillingHandler>.Instance);
        }

        public FakeInvoiceRepository Invoices { get; } = new();
        public FakeTariffRepository Tariffs { get; } = new();
        public FakeProcessedMessageRepository Processed { get; } = new();
        public FakeUnitOfWork UnitOfWork { get; } = new();
        public AppointmentBillingHandler Handler { get; }
    }

    private sealed class FixedTimeProvider(DateTime utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utcNow);
    }

    private sealed class FakeInvoiceRepository : IInvoiceRepository
    {
        public Invoice? Stored { get; set; }
        public List<Invoice> Added { get; } = [];
        public int AppointmentLookupCalls { get; private set; }

        public Task<Invoice?> GetTrackedByAppointmentIdAsync(Guid appointmentId, CancellationToken cancellationToken = default)
        {
            AppointmentLookupCalls++;
            return Task.FromResult(Stored?.AppointmentId == appointmentId ? Stored : null);
        }

        public Task<Invoice?> GetByIdAsync(Guid invoiceId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Stored?.InvoiceId == invoiceId ? Stored : null);

        public Task<Invoice?> GetByAppointmentIdAsync(Guid appointmentId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Stored?.AppointmentId == appointmentId ? Stored : null);

        public Task AddAsync(Invoice invoice, CancellationToken cancellationToken = default)
        {
            Added.Add(invoice);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeTariffRepository : IServiceTariffRepository
    {
        public ServiceTariff? Result { get; set; }
        public int LookupCalls { get; private set; }

        public Task<ServiceTariff?> FindEffectiveAsync(
            string serviceCode,
            DateTime effectiveAtUtc,
            CancellationToken cancellationToken = default)
        {
            LookupCalls++;
            return Task.FromResult(Result);
        }
    }

    private sealed class FakeProcessedMessageRepository : IProcessedMessageRepository
    {
        public bool Exists { get; set; }
        public int ExistsCalls { get; private set; }
        public List<ProcessedMessage> Added { get; } = [];

        public Task<bool> ExistsAsync(Guid messageId, CancellationToken cancellationToken = default)
        {
            ExistsCalls++;
            return Task.FromResult(Exists);
        }

        public Task AddAsync(ProcessedMessage message, CancellationToken cancellationToken = default)
        {
            Added.Add(message);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeUnitOfWork : IUnitOfWork
    {
        public int SaveCount { get; private set; }

        public Task SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            SaveCount++;
            return Task.CompletedTask;
        }
    }
}
