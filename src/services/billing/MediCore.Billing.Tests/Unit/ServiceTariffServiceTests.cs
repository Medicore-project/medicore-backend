using MediCore.Billing.Application.DTOs;
using MediCore.Billing.Application.Entities;
using MediCore.Billing.Application.Interfaces;
using MediCore.Billing.Application.Services;
using MediCore.Billing.Application.Validators;

namespace MediCore.Billing.Tests.Unit;

public sealed class ServiceTariffServiceTests
{
    private static readonly DateTime FirstEffective = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Create_normalizes_code_and_currency()
    {
        var fixture = new Fixture();

        var result = await fixture.Service.CreateAsync(new CreateServiceTariffRequest(
            " lab-cbc ", "Complete blood count", 1800m, "lkr", FirstEffective));

        var created = Assert.IsType<ServiceTariffCreatedResult>(result).Tariff;
        Assert.Equal("LAB-CBC", created.ServiceCode);
        Assert.Equal("LKR", created.Currency);
        Assert.True(created.IsActive);
        Assert.Equal(1, fixture.UnitOfWork.SaveCount);
    }

    [Fact]
    public async Task Duplicate_code_is_rejected_without_saving()
    {
        var fixture = new Fixture(Tariff());

        var result = await fixture.Service.CreateAsync(new CreateServiceTariffRequest(
            "gen-consult", "Duplicate", 3000m, "LKR", FirstEffective.AddDays(1)));

        var duplicate = Assert.IsType<DuplicateServiceTariffCodeResult>(result);
        Assert.Equal("GEN-CONSULT", duplicate.ServiceCode);
        Assert.Equal(0, fixture.UnitOfWork.SaveCount);
        Assert.Single(fixture.Repository.Items);
    }

    [Fact]
    public async Task Price_change_closes_old_version_and_keeps_issued_invoice_price()
    {
        var original = Tariff();
        var issuedInvoice = new Invoice
        {
            Lines =
            {
                new InvoiceLine
                {
                    TariffId = original.TariffId,
                    ServiceCode = original.ServiceCode,
                    Description = original.Description,
                    Quantity = 1,
                    UnitPrice = original.UnitPrice,
                    LineTotal = original.UnitPrice
                }
            }
        };
        var fixture = new Fixture(original);
        var changedAt = FirstEffective.AddMonths(6);

        var result = await fixture.Service.UpdatePriceAsync(
            original.TariffId,
            new UpdateServiceTariffRequest("General consultation", 3000m, "LKR", changedAt));

        var replacement = Assert.IsType<ServiceTariffVersionCreatedResult>(result).Tariff;
        Assert.Equal(changedAt, original.EffectiveToUtc);
        Assert.Equal(3000m, replacement.UnitPrice);
        Assert.NotEqual(original.TariffId, replacement.TariffId);
        Assert.Equal(2500m, Assert.Single(issuedInvoice.Lines).UnitPrice);
        Assert.Equal(1, fixture.UnitOfWork.SaveCount);
    }

    [Fact]
    public async Task Price_change_must_be_after_latest_version()
    {
        var original = Tariff();
        var fixture = new Fixture(original);

        var result = await fixture.Service.UpdatePriceAsync(
            original.TariffId,
            new UpdateServiceTariffRequest("General consultation", 3000m, "LKR", FirstEffective));

        var conflict = Assert.IsType<ServiceTariffEffectiveDateConflictResult>(result);
        Assert.Equal(FirstEffective, conflict.LatestEffectiveFromUtc);
        Assert.Equal(0, fixture.UnitOfWork.SaveCount);
    }

    [Fact]
    public async Task Deactivation_blocks_effective_lookup_without_changing_an_issued_invoice()
    {
        var original = Tariff();
        var fixture = new Fixture(original);
        var issuedLine = new InvoiceLine { TariffId = original.TariffId, UnitPrice = original.UnitPrice };

        var result = await fixture.Service.DeactivateAsync(original.TariffId);
        var available = await fixture.Repository.FindEffectiveAsync(original.ServiceCode, FirstEffective.AddDays(1));

        Assert.Equal(DeactivateServiceTariffResult.Deactivated, result);
        Assert.Null(available);
        Assert.False(original.IsActive);
        Assert.Equal(2500m, issuedLine.UnitPrice);
    }

    [Fact]
    public async Task Validators_reject_invalid_code_price_currency_and_date()
    {
        var result = await new CreateServiceTariffRequestValidator().ValidateAsync(
            new CreateServiceTariffRequest("bad code", "", 0m, "rupees", DateTime.MinValue));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.PropertyName == "ServiceCode");
        Assert.Contains(result.Errors, error => error.PropertyName == "Description");
        Assert.Contains(result.Errors, error => error.PropertyName == "UnitPrice");
        Assert.Contains(result.Errors, error => error.PropertyName == "Currency");
        Assert.Contains(result.Errors, error => error.PropertyName == "EffectiveFromUtc");
    }

    private static ServiceTariff Tariff() => new()
    {
        ServiceCode = "GEN-CONSULT",
        Description = "General consultation",
        UnitPrice = 2500m,
        Currency = "LKR",
        EffectiveFromUtc = FirstEffective,
        IsActive = true
    };

    private sealed class Fixture(params ServiceTariff[] tariffs)
    {
        public FakeTariffRepository Repository { get; } = new(tariffs);
        public FakeUnitOfWork UnitOfWork { get; } = new();
        public ServiceTariffService Service => new(Repository, UnitOfWork);
    }

    private sealed class FakeTariffRepository(params ServiceTariff[] tariffs) : IServiceTariffRepository
    {
        public List<ServiceTariff> Items { get; } = [.. tariffs];

        public Task<IReadOnlyList<ServiceTariff>> GetAllAsync(
            bool includeInactive,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ServiceTariff>>(
                Items.Where(item => includeInactive || item.IsActive).ToArray());

        public Task<ServiceTariff?> GetByIdAsync(
            Guid tariffId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Items.FirstOrDefault(item => item.TariffId == tariffId));

        public Task<bool> CodeExistsAsync(
            string serviceCode,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Items.Any(item => item.ServiceCode == serviceCode));

        public Task<IReadOnlyList<ServiceTariff>> GetVersionsAsync(
            string serviceCode,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ServiceTariff>>(
                Items.Where(item => item.ServiceCode == serviceCode).ToArray());

        public Task<ServiceTariff?> FindEffectiveAsync(
            string serviceCode,
            DateTime effectiveAtUtc,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Items
                .Where(item => item.ServiceCode == serviceCode
                    && item.IsActive
                    && item.EffectiveFromUtc <= effectiveAtUtc
                    && (item.EffectiveToUtc is null || item.EffectiveToUtc > effectiveAtUtc))
                .MaxBy(item => item.EffectiveFromUtc));

        public Task AddAsync(ServiceTariff tariff, CancellationToken cancellationToken = default)
        {
            Items.Add(tariff);
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
