using MediCore.Billing.Application.DTOs;
using MediCore.Billing.Application.Entities;
using MediCore.Billing.Application.Interfaces;
using MediCore.Billing.Application.Services;
using MediCore.Billing.Application.Validators;

namespace MediCore.Billing.Tests.Unit;

public sealed class NotificationTemplateServiceTests
{
    private static readonly DateTime NowUtc = new(2026, 10, 8, 9, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Create_normalizes_code_and_persists_template()
    {
        var fixture = new Fixture();

        var result = await fixture.Service.CreateAsync(new CreateNotificationTemplateRequest(
            " custom_notice ", " Custom notice ", " Subject ", " Body "));

        var created = Assert.IsType<NotificationTemplateCreatedResult>(result).Template;
        Assert.Equal("CUSTOM_NOTICE", created.Code);
        Assert.Equal("Custom notice", created.Name);
        Assert.Equal(NowUtc, created.CreatedAtUtc);
        Assert.Equal(1, fixture.UnitOfWork.SaveCount);
    }

    [Fact]
    public async Task Duplicate_code_is_rejected_without_saving()
    {
        var fixture = new Fixture(new NotificationTemplate { Code = "PATIENT_WELCOME" });

        var result = await fixture.Service.CreateAsync(new CreateNotificationTemplateRequest(
            "patient_welcome", "Duplicate", "Subject", "Body"));

        Assert.IsType<DuplicateNotificationTemplateCodeResult>(result);
        Assert.Equal(0, fixture.UnitOfWork.SaveCount);
    }

    [Fact]
    public async Task Update_and_deactivate_keep_template_history_row()
    {
        var template = new NotificationTemplate
        {
            NotificationTemplateId = Guid.NewGuid(),
            Code = "PAYMENT_RECEIPT",
            Name = "Receipt",
            SubjectTemplate = "Old",
            BodyTemplate = "Old body",
            IsActive = true
        };
        var fixture = new Fixture(template);

        var updated = await fixture.Service.UpdateAsync(
            template.NotificationTemplateId,
            new UpdateNotificationTemplateRequest("Receipt email", "New", "New body", true));
        var deactivated = await fixture.Service.DeactivateAsync(template.NotificationTemplateId);

        Assert.IsType<NotificationTemplateUpdatedResult>(updated);
        Assert.True(deactivated);
        Assert.False(template.IsActive);
        Assert.Equal("New", template.SubjectTemplate);
        Assert.Single(fixture.Repository.Items);
        Assert.Equal(2, fixture.UnitOfWork.SaveCount);
    }

    [Fact]
    public async Task Validators_reject_invalid_template_content()
    {
        var create = await new CreateNotificationTemplateRequestValidator().ValidateAsync(
            new CreateNotificationTemplateRequest("bad code!", "", "", ""));
        var update = await new UpdateNotificationTemplateRequestValidator().ValidateAsync(
            new UpdateNotificationTemplateRequest("", "", "", true));

        Assert.False(create.IsValid);
        Assert.False(update.IsValid);
    }

    private sealed class Fixture
    {
        public Fixture(params NotificationTemplate[] templates)
        {
            Repository.Items.AddRange(templates);
            Service = new NotificationTemplateService(
                Repository,
                new EmptyLogRepository(),
                UnitOfWork,
                new FixedTimeProvider(NowUtc));
        }

        public FakeTemplateRepository Repository { get; } = new();
        public FakeUnitOfWork UnitOfWork { get; } = new();
        public NotificationTemplateService Service { get; }
    }

    private sealed class FixedTimeProvider(DateTime utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utcNow);
    }

    private sealed class FakeTemplateRepository : INotificationTemplateRepository
    {
        public List<NotificationTemplate> Items { get; } = [];
        public Task<IReadOnlyList<NotificationTemplate>> GetAllAsync(bool includeInactive, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<NotificationTemplate>>(Items);
        public Task<NotificationTemplate?> GetByIdAsync(Guid templateId, CancellationToken cancellationToken = default) => Task.FromResult(Items.FirstOrDefault(item => item.NotificationTemplateId == templateId));
        public Task<NotificationTemplate?> GetByCodeAsync(string code, CancellationToken cancellationToken = default) => Task.FromResult(Items.FirstOrDefault(item => item.Code == code));
        public Task<bool> CodeExistsAsync(string code, Guid? excludingId = null, CancellationToken cancellationToken = default) => Task.FromResult(Items.Any(item => item.Code == code && (!excludingId.HasValue || item.NotificationTemplateId != excludingId.Value)));
        public Task AddAsync(NotificationTemplate template, CancellationToken cancellationToken = default) { Items.Add(template); return Task.CompletedTask; }
    }

    private sealed class EmptyLogRepository : INotificationLogRepository
    {
        public Task<NotificationLog?> GetBySourceAsync(Guid sourceMessageId, string templateCode, CancellationToken cancellationToken = default) => Task.FromResult<NotificationLog?>(null);
        public Task<IReadOnlyList<NotificationLog>> GetRecentAsync(int limit, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<NotificationLog>>([]);
        public Task AddAsync(NotificationLog notification, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FakeUnitOfWork : IUnitOfWork
    {
        public int SaveCount { get; private set; }
        public Task SaveChangesAsync(CancellationToken cancellationToken = default) { SaveCount++; return Task.CompletedTask; }
    }
}
