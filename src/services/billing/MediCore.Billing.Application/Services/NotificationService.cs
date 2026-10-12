using System.Globalization;
using System.Net.Mail;
using MediCore.Billing.Application.Entities;
using MediCore.Billing.Application.Exceptions;
using MediCore.Billing.Application.Interfaces;
using MediCore.Contracts.Events;
using MediCore.Contracts.Events.Appointment;
using MediCore.Contracts.Events.Billing;
using MediCore.Contracts.Events.Patient;

namespace MediCore.Billing.Application.Services;

public sealed class NotificationService : INotificationService
{
    public const string WelcomeTemplate = "PATIENT_WELCOME";
    public const string AppointmentTemplate = "APPOINTMENT_CONFIRMATION";
    public const string ReceiptTemplate = "PAYMENT_RECEIPT";

    private readonly INotificationTemplateRepository _templates;
    private readonly INotificationLogRepository _logs;
    private readonly IPatientContactRepository _contacts;
    private readonly IEmailSender _emailSender;
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _timeProvider;

    public NotificationService(
        INotificationTemplateRepository templates,
        INotificationLogRepository logs,
        IPatientContactRepository contacts,
        IEmailSender emailSender,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider)
    {
        _templates = templates;
        _logs = logs;
        _contacts = contacts;
        _emailSender = emailSender;
        _unitOfWork = unitOfWork;
        _timeProvider = timeProvider;
    }

    public async Task<NotificationDeliveryResult> HandlePatientRegisteredAsync(
        PatientRegisteredEvent integrationEvent,
        CancellationToken cancellationToken = default)
    {
        ValidateEvent(integrationEvent, integrationEvent.PatientId);
        if (await WasSentAsync(integrationEvent.MessageId, WelcomeTemplate, cancellationToken))
        {
            return NotificationDeliveryResult.Duplicate;
        }
        ValidateEmail(integrationEvent.Email);

        var contact = await _contacts.GetAsync(integrationEvent.PatientId, cancellationToken);
        if (contact is null)
        {
            contact = new PatientContact { PatientId = integrationEvent.PatientId };
            await _contacts.AddAsync(contact, cancellationToken);
        }

        contact.FullName = integrationEvent.FullName.Trim();
        contact.Email = integrationEvent.Email.Trim();
        contact.UpdatedAtUtc = _timeProvider.GetUtcNow().UtcDateTime;
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return await DeliverAsync(
            integrationEvent,
            WelcomeTemplate,
            contact,
            new Dictionary<string, string>
            {
                ["patientName"] = contact.FullName,
                ["patientId"] = contact.PatientId.ToString()
            },
            cancellationToken);
    }

    public async Task<NotificationDeliveryResult> HandleAppointmentBookedAsync(
        AppointmentBookedEvent integrationEvent,
        CancellationToken cancellationToken = default)
    {
        ValidateEvent(integrationEvent, integrationEvent.PatientId);
        if (await WasSentAsync(integrationEvent.MessageId, AppointmentTemplate, cancellationToken))
        {
            return NotificationDeliveryResult.Duplicate;
        }
        var contact = await RequiredContactAsync(integrationEvent.PatientId, cancellationToken);
        return await DeliverAsync(
            integrationEvent,
            AppointmentTemplate,
            contact,
            new Dictionary<string, string>
            {
                ["patientName"] = contact.FullName,
                ["appointmentId"] = integrationEvent.AppointmentId.ToString(),
                ["doctorId"] = integrationEvent.DoctorId.ToString(),
                ["slotStart"] = integrationEvent.SlotStart.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
                ["serviceCode"] = integrationEvent.ServiceCode.Trim()
            },
            cancellationToken);
    }

    public async Task<NotificationDeliveryResult> HandleInvoicePaidAsync(
        InvoicePaidEvent integrationEvent,
        CancellationToken cancellationToken = default)
    {
        ValidateEvent(integrationEvent, integrationEvent.PatientId);
        if (await WasSentAsync(integrationEvent.MessageId, ReceiptTemplate, cancellationToken))
        {
            return NotificationDeliveryResult.Duplicate;
        }
        var contact = await RequiredContactAsync(integrationEvent.PatientId, cancellationToken);
        return await DeliverAsync(
            integrationEvent,
            ReceiptTemplate,
            contact,
            new Dictionary<string, string>
            {
                ["patientName"] = contact.FullName,
                ["invoiceId"] = integrationEvent.InvoiceId.ToString(),
                ["amount"] = integrationEvent.Amount.ToString("0.00", CultureInfo.InvariantCulture),
                ["method"] = integrationEvent.Method.Trim()
            },
            cancellationToken);
    }

    private async Task<NotificationDeliveryResult> DeliverAsync(
        IntegrationEvent integrationEvent,
        string templateCode,
        PatientContact contact,
        IReadOnlyDictionary<string, string> tokens,
        CancellationToken cancellationToken)
    {
        var template = await _templates.GetByCodeAsync(templateCode, cancellationToken);
        if (template is null || !template.IsActive)
        {
            throw new NotificationDeliveryException($"Active notification template '{templateCode}' was not found.");
        }

        var subject = Render(template.SubjectTemplate, tokens);
        var body = Render(template.BodyTemplate, tokens);
        var log = await _logs.GetBySourceAsync(integrationEvent.MessageId, templateCode, cancellationToken);
        if (log is not null && string.Equals(log.Status, NotificationStatuses.Sent, StringComparison.Ordinal))
        {
            return NotificationDeliveryResult.Duplicate;
        }

        if (log is null)
        {
            log = new NotificationLog
            {
                NotificationTemplateId = template.NotificationTemplateId,
                SourceMessageId = integrationEvent.MessageId,
                EventType = integrationEvent.EventType,
                TemplateCode = templateCode,
                Recipient = contact.Email,
                Subject = subject,
                Body = body,
                CorrelationId = integrationEvent.CorrelationId,
                CreatedAtUtc = _timeProvider.GetUtcNow().UtcDateTime
            };
            await _logs.AddAsync(log, cancellationToken);
            try
            {
                await _unitOfWork.SaveChangesAsync(cancellationToken);
            }
            catch (DuplicateNotificationLogException)
            {
                return NotificationDeliveryResult.Duplicate;
            }
        }

        log.Recipient = contact.Email;
        log.Subject = subject;
        log.Body = body;
        log.AttemptCount++;
        log.LastAttemptAtUtc = _timeProvider.GetUtcNow().UtcDateTime;

        try
        {
            await _emailSender.SendAsync(log.Recipient, log.Subject, log.Body, cancellationToken);
            log.Status = NotificationStatuses.Sent;
            log.SentAtUtc = _timeProvider.GetUtcNow().UtcDateTime;
            log.Error = null;
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return NotificationDeliveryResult.Sent;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            log.Status = NotificationStatuses.Failed;
            log.Error = exception.Message.Length > 2_000 ? exception.Message[..2_000] : exception.Message;
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            throw new NotificationDeliveryException(
                $"Email delivery for '{integrationEvent.EventType}' failed.",
                exception);
        }
    }

    private async Task<PatientContact> RequiredContactAsync(Guid patientId, CancellationToken cancellationToken)
    {
        var contact = await _contacts.GetAsync(patientId, cancellationToken);
        if (contact is null)
        {
            throw new NotificationDeliveryException($"No notification contact is cached for patient '{patientId}'.");
        }

        ValidateEmail(contact.Email);
        return contact;
    }

    private async Task<bool> WasSentAsync(
        Guid messageId,
        string templateCode,
        CancellationToken cancellationToken)
    {
        var existing = await _logs.GetBySourceAsync(messageId, templateCode, cancellationToken);
        return existing is not null && string.Equals(existing.Status, NotificationStatuses.Sent, StringComparison.Ordinal);
    }

    private static string Render(string template, IReadOnlyDictionary<string, string> tokens)
    {
        var rendered = template;
        foreach (var token in tokens)
        {
            rendered = rendered.Replace($"{{{{{token.Key}}}}}", token.Value, StringComparison.OrdinalIgnoreCase);
        }

        return rendered;
    }

    private static void ValidateEvent(IntegrationEvent integrationEvent, Guid patientId)
    {
        if (integrationEvent.MessageId == Guid.Empty) throw new ArgumentException("MessageId is required.");
        if (patientId == Guid.Empty) throw new ArgumentException("PatientId is required.");
        if (integrationEvent.Version != 1) throw new ArgumentException("Only event version 1 is supported.");
    }

    private static void ValidateEmail(string email)
    {
        if (!MailAddress.TryCreate(email?.Trim(), out _))
        {
            throw new NotificationDeliveryException("The patient email address is invalid.");
        }
    }
}
