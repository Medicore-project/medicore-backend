using MediCore.Billing.Application.Entities;
using MediCore.Billing.Application.Exceptions;
using MediCore.Billing.Application.Interfaces;
using MediCore.Billing.Application.Services;
using MediCore.Contracts.Events.Appointment;
using MediCore.Contracts.Events.Billing;
using MediCore.Contracts.Events.Patient;

namespace MediCore.Billing.Tests.Unit;

public sealed class NotificationServiceTests
{
    private static readonly DateTime NowUtc = new(2026, 10, 8, 8, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Patient_registration_caches_contact_and_sends_rendered_welcome()
    {
        var fixture = new Fixture();
        fixture.Templates.Add(Template(NotificationService.WelcomeTemplate, "Welcome {{patientName}}", "Patient {{patientId}}"));
        var registered = new PatientRegisteredEvent
        {
            MessageId = Guid.NewGuid(),
            PatientId = Guid.NewGuid(),
            FullName = "  Amara Silva  ",
            Email = "amara@example.com",
            CorrelationId = "corr-welcome"
        };

        var result = await fixture.Service.HandlePatientRegisteredAsync(registered);

        Assert.Equal(NotificationDeliveryResult.Sent, result);
        var contact = Assert.Single(fixture.Contacts.Items);
        Assert.Equal("Amara Silva", contact.FullName);
        var email = Assert.Single(fixture.Email.Sent);
        Assert.Equal("amara@example.com", email.Recipient);
        Assert.Equal("Welcome Amara Silva", email.Subject);
        Assert.Contains(registered.PatientId.ToString(), email.Body);
        var log = Assert.Single(fixture.Logs.Items);
        Assert.Equal(NotificationStatuses.Sent, log.Status);
        Assert.Equal(1, log.AttemptCount);
        Assert.Equal(3, fixture.UnitOfWork.SaveCount);
    }

    [Fact]
    public async Task Appointment_booking_uses_cached_contact_and_sends_confirmation()
    {
        var patientId = Guid.NewGuid();
        var fixture = new Fixture(new PatientContact
        {
            PatientId = patientId,
            FullName = "Nimal Perera",
            Email = "nimal@example.com"
        });
        fixture.Templates.Add(Template(
            NotificationService.AppointmentTemplate,
            "Appointment {{serviceCode}}",
            "Hello {{patientName}} at {{slotStart}}"));
        var booked = new AppointmentBookedEvent
        {
            MessageId = Guid.NewGuid(),
            AppointmentId = Guid.NewGuid(),
            PatientId = patientId,
            DoctorId = Guid.NewGuid(),
            SlotStart = NowUtc.AddDays(1),
            ServiceCode = "GEN-CONSULT"
        };

        var result = await fixture.Service.HandleAppointmentBookedAsync(booked);

        Assert.Equal(NotificationDeliveryResult.Sent, result);
        var sent = Assert.Single(fixture.Email.Sent);
        Assert.Equal("Appointment GEN-CONSULT", sent.Subject);
        Assert.Contains("Nimal Perera", sent.Body);
    }

    [Fact]
    public async Task Sent_notification_is_idempotent_on_redelivery()
    {
        var patientId = Guid.NewGuid();
        var paid = new InvoicePaidEvent
        {
            MessageId = Guid.NewGuid(),
            InvoiceId = Guid.NewGuid(),
            PatientId = patientId,
            Amount = 2500m,
            Method = "Card"
        };
        var fixture = new Fixture(new PatientContact
        {
            PatientId = patientId,
            FullName = "Saman Jay",
            Email = "saman@example.com"
        });
        var template = Template(NotificationService.ReceiptTemplate, "Receipt", "{{amount}} by {{method}}");
        fixture.Templates.Add(template);
        fixture.Logs.Items.Add(new NotificationLog
        {
            NotificationTemplateId = template.NotificationTemplateId,
            SourceMessageId = paid.MessageId,
            TemplateCode = NotificationService.ReceiptTemplate,
            EventType = paid.EventType,
            Recipient = "saman@example.com",
            Subject = "Receipt",
            Body = "2500.00 by Card",
            Status = NotificationStatuses.Sent
        });

        var result = await fixture.Service.HandleInvoicePaidAsync(paid);

        Assert.Equal(NotificationDeliveryResult.Duplicate, result);
        Assert.Empty(fixture.Email.Sent);
    }

    [Fact]
    public async Task Failed_send_is_logged_and_rethrown_for_retry_routing()
    {
        var patientId = Guid.NewGuid();
        var fixture = new Fixture(new PatientContact
        {
            PatientId = patientId,
            FullName = "Test Patient",
            Email = "patient@example.com"
        });
        fixture.Templates.Add(Template(NotificationService.ReceiptTemplate, "Receipt", "Paid {{amount}}"));
        fixture.Email.Exception = new InvalidOperationException("SMTP unavailable");
        var paid = new InvoicePaidEvent
        {
            MessageId = Guid.NewGuid(),
            InvoiceId = Guid.NewGuid(),
            PatientId = patientId,
            Amount = 100m,
            Method = "Cash"
        };

        await Assert.ThrowsAsync<NotificationDeliveryException>(() =>
            fixture.Service.HandleInvoicePaidAsync(paid));

        var log = Assert.Single(fixture.Logs.Items);
        Assert.Equal(NotificationStatuses.Failed, log.Status);
        Assert.Equal(1, log.AttemptCount);
        Assert.Contains("SMTP unavailable", log.Error);
    }

    [Fact]
    public async Task Missing_patient_contact_is_retryable_failure()
    {
        var fixture = new Fixture();
        var booked = new AppointmentBookedEvent
        {
            MessageId = Guid.NewGuid(),
            AppointmentId = Guid.NewGuid(),
            PatientId = Guid.NewGuid(),
            DoctorId = Guid.NewGuid(),
            SlotStart = NowUtc,
            ServiceCode = "GEN-CONSULT"
        };

        var exception = await Assert.ThrowsAsync<NotificationDeliveryException>(() =>
            fixture.Service.HandleAppointmentBookedAsync(booked));

        Assert.Contains("No notification contact", exception.Message);
        Assert.Empty(fixture.Email.Sent);
    }

    private static NotificationTemplate Template(string code, string subject, string body) => new()
    {
        NotificationTemplateId = Guid.NewGuid(),
        Code = code,
        Name = code,
        SubjectTemplate = subject,
        BodyTemplate = body,
        IsActive = true,
        CreatedAtUtc = NowUtc
    };

    private sealed class Fixture
    {
        public Fixture(params PatientContact[] contacts)
        {
            Contacts.Items.AddRange(contacts);
            Service = new NotificationService(
                Templates,
                Logs,
                Contacts,
                Email,
                UnitOfWork,
                new FixedTimeProvider(NowUtc));
        }

        public FakeTemplateRepository Templates { get; } = new();
        public FakeLogRepository Logs { get; } = new();
        public FakeContactRepository Contacts { get; } = new();
        public FakeEmailSender Email { get; } = new();
        public FakeUnitOfWork UnitOfWork { get; } = new();
        public NotificationService Service { get; }
    }

    private sealed class FixedTimeProvider(DateTime utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utcNow);
    }

    private sealed class FakeTemplateRepository : INotificationTemplateRepository
    {
        private readonly List<NotificationTemplate> _items = [];
        public void Add(NotificationTemplate template) => _items.Add(template);
        public Task<IReadOnlyList<NotificationTemplate>> GetAllAsync(bool includeInactive, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<NotificationTemplate>>(_items);
        public Task<NotificationTemplate?> GetByIdAsync(Guid templateId, CancellationToken cancellationToken = default) => Task.FromResult(_items.FirstOrDefault(item => item.NotificationTemplateId == templateId));
        public Task<NotificationTemplate?> GetByCodeAsync(string code, CancellationToken cancellationToken = default) => Task.FromResult(_items.FirstOrDefault(item => item.Code == code));
        public Task<bool> CodeExistsAsync(string code, Guid? excludingId = null, CancellationToken cancellationToken = default) => Task.FromResult(_items.Any(item => item.Code == code && item.NotificationTemplateId != excludingId));
        public Task AddAsync(NotificationTemplate template, CancellationToken cancellationToken = default) { _items.Add(template); return Task.CompletedTask; }
    }

    private sealed class FakeLogRepository : INotificationLogRepository
    {
        public List<NotificationLog> Items { get; } = [];
        public Task<NotificationLog?> GetBySourceAsync(Guid sourceMessageId, string templateCode, CancellationToken cancellationToken = default) => Task.FromResult(Items.FirstOrDefault(item => item.SourceMessageId == sourceMessageId && item.TemplateCode == templateCode));
        public Task<IReadOnlyList<NotificationLog>> GetRecentAsync(int limit, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<NotificationLog>>(Items.Take(limit).ToArray());
        public Task AddAsync(NotificationLog notification, CancellationToken cancellationToken = default) { Items.Add(notification); return Task.CompletedTask; }
    }

    private sealed class FakeContactRepository : IPatientContactRepository
    {
        public List<PatientContact> Items { get; } = [];
        public Task<PatientContact?> GetAsync(Guid patientId, CancellationToken cancellationToken = default) => Task.FromResult(Items.FirstOrDefault(item => item.PatientId == patientId));
        public Task AddAsync(PatientContact contact, CancellationToken cancellationToken = default) { Items.Add(contact); return Task.CompletedTask; }
    }

    private sealed class FakeEmailSender : IEmailSender
    {
        public List<(string Recipient, string Subject, string Body)> Sent { get; } = [];
        public Exception? Exception { get; set; }
        public Task SendAsync(string recipient, string subject, string body, CancellationToken cancellationToken = default)
        {
            if (Exception is not null) throw Exception;
            Sent.Add((recipient, subject, body));
            return Task.CompletedTask;
        }
    }

    private sealed class FakeUnitOfWork : IUnitOfWork
    {
        public int SaveCount { get; private set; }
        public Task SaveChangesAsync(CancellationToken cancellationToken = default) { SaveCount++; return Task.CompletedTask; }
    }
}
