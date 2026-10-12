using MediCore.Billing.Application.Entities;
using MediCore.Billing.Application.Exceptions;
using MediCore.Billing.Application.Interfaces;
using MediCore.Contracts.Events;
using MediCore.Contracts.Events.Appointment;
using Microsoft.Extensions.Logging;

namespace MediCore.Billing.Application.Services;

public sealed class AppointmentBillingHandler : IAppointmentBillingHandler
{
    public const string ConsumerGroup = "medicore-billing";
    public const string SourceTopic = "appointment-events";

    /// <summary>
    /// The tariff a specialization is billed at until it has one of its own. The appointment
    /// service names each specialization's code <c>SPEC-&lt;NAME&gt;</c>; this is the same code
    /// with no specialization in it.
    /// </summary>
    public const string DefaultSpecialistServiceCode = "SPEC-CONSULT";

    private const string SpecializationCodePrefix = "SPEC-";

    private readonly IInvoiceRepository _invoiceRepository;
    private readonly IServiceTariffRepository _tariffRepository;
    private readonly IProcessedMessageRepository _processedMessageRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<AppointmentBillingHandler> _logger;

    public AppointmentBillingHandler(
        IInvoiceRepository invoiceRepository,
        IServiceTariffRepository tariffRepository,
        IProcessedMessageRepository processedMessageRepository,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider,
        ILogger<AppointmentBillingHandler> logger)
    {
        _invoiceRepository = invoiceRepository;
        _tariffRepository = tariffRepository;
        _processedMessageRepository = processedMessageRepository;
        _unitOfWork = unitOfWork;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<AppointmentBillingResult> HandleBookedAsync(
        AppointmentBookedEvent bookedEvent,
        CancellationToken cancellationToken = default)
    {
        // This must remain the first external operation: duplicate deliveries do no business work.
        if (await _processedMessageRepository.ExistsAsync(bookedEvent.MessageId, cancellationToken))
        {
            return AppointmentBillingResult.Duplicate;
        }

        ValidateBooked(bookedEvent);

        if (await _invoiceRepository.GetTrackedByAppointmentIdAsync(
                bookedEvent.AppointmentId, cancellationToken) is not null)
        {
            await RecordProcessedAsync(bookedEvent, AppointmentBillingResult.InvoiceAlreadyExists, cancellationToken);
            return await SaveAsync(AppointmentBillingResult.InvoiceAlreadyExists, cancellationToken);
        }

        var serviceCode = bookedEvent.ServiceCode.Trim();
        var pricedAtUtc = EnsureUtc(bookedEvent.SlotStart);
        var tariff = await _tariffRepository.FindEffectiveAsync(serviceCode, pricedAtUtc, cancellationToken);

        // A specialization with no tariff of its own is still a specialist visit, so it is billed
        // at the default specialist tariff instead of being left unpriced. The invoice keeps the
        // specialization's own code; only the price is borrowed.
        if (tariff is null && IsSpecializationCode(serviceCode))
        {
            tariff = await _tariffRepository.FindEffectiveAsync(
                DefaultSpecialistServiceCode, pricedAtUtc, cancellationToken);
        }

        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var invoiceId = Guid.NewGuid();
        var missingTariff = tariff is null;
        var unitPrice = tariff?.UnitPrice;

        var invoice = new Invoice
        {
            InvoiceId = invoiceId,
            InvoiceNumber = CreateInvoiceNumber(invoiceId, nowUtc),
            AppointmentId = bookedEvent.AppointmentId,
            PatientId = bookedEvent.PatientId,
            DepartmentId = bookedEvent.DepartmentId,
            ServiceCode = serviceCode,
            Status = InvoiceStatus.Draft,
            Currency = tariff?.Currency ?? "LKR",
            Subtotal = unitPrice ?? 0m,
            Total = unitPrice ?? 0m,
            RequiresManualPricing = missingTariff,
            PricingIssue = missingTariff ? $"No effective tariff for '{serviceCode}'." : null,
            IssuedAtUtc = EnsureUtc(bookedEvent.OccurredAtUtc),
            CreatedAtUtc = nowUtc,
            Lines =
            [
                new InvoiceLine
                {
                    InvoiceId = invoiceId,
                    TariffId = tariff?.TariffId,
                    ServiceCode = serviceCode,
                    Description = tariff?.Description ?? $"Manual pricing required: {serviceCode}",
                    Quantity = 1,
                    UnitPrice = unitPrice,
                    LineTotal = unitPrice ?? 0m
                }
            ]
        };

        await _invoiceRepository.AddAsync(invoice, cancellationToken);
        var result = missingTariff
            ? AppointmentBillingResult.MissingTariff
            : AppointmentBillingResult.Processed;
        await RecordProcessedAsync(bookedEvent, result, cancellationToken);

        result = await SaveAsync(result, cancellationToken);
        _logger.LogInformation(
            "Created draft invoice {InvoiceId} for appointment {AppointmentId}; outcome {Outcome}.",
            invoice.InvoiceId,
            invoice.AppointmentId,
            result);
        return result;
    }

    public async Task<AppointmentBillingResult> HandleCompletedAsync(
        AppointmentCompletedEvent completedEvent,
        CancellationToken cancellationToken = default)
    {
        // As for booking, deduplication is deliberately checked before validation or invoice access.
        if (await _processedMessageRepository.ExistsAsync(completedEvent.MessageId, cancellationToken))
        {
            return AppointmentBillingResult.Duplicate;
        }

        ValidateCompleted(completedEvent);
        var invoice = await _invoiceRepository.GetTrackedByAppointmentIdAsync(
            completedEvent.AppointmentId, cancellationToken);

        if (invoice is null)
        {
            await RecordProcessedAsync(completedEvent, AppointmentBillingResult.InvoiceNotFound, cancellationToken);
            _logger.LogWarning(
                "Completion message {MessageId} has no invoice for appointment {AppointmentId}.",
                completedEvent.MessageId,
                completedEvent.AppointmentId);
            return await SaveAsync(AppointmentBillingResult.InvoiceNotFound, cancellationToken);
        }

        if (!string.Equals(invoice.Status, InvoiceStatus.Draft, StringComparison.Ordinal))
        {
            await RecordProcessedAsync(completedEvent, AppointmentBillingResult.AlreadyFinalized, cancellationToken);
            return await SaveAsync(AppointmentBillingResult.AlreadyFinalized, cancellationToken);
        }

        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        invoice.Status = InvoiceStatus.Payable;
        invoice.FinalizedAtUtc = EnsureUtc(completedEvent.OccurredAtUtc);
        invoice.UpdatedAtUtc = nowUtc;
        invoice.Version++;
        await RecordProcessedAsync(completedEvent, AppointmentBillingResult.Processed, cancellationToken);

        var result = await SaveAsync(AppointmentBillingResult.Processed, cancellationToken);
        _logger.LogInformation(
            "Finalized invoice {InvoiceId} for appointment {AppointmentId} as Payable.",
            invoice.InvoiceId,
            invoice.AppointmentId);
        return result;
    }

    public async Task<AppointmentBillingResult> HandleCancelledAsync(
        AppointmentCancelledEvent cancelledEvent,
        CancellationToken cancellationToken = default)
    {
        // Duplicate cancellation deliveries must not inspect or mutate the invoice.
        if (await _processedMessageRepository.ExistsAsync(cancelledEvent.MessageId, cancellationToken))
        {
            return AppointmentBillingResult.Duplicate;
        }

        ValidateCancelled(cancelledEvent);
        var invoice = await _invoiceRepository.GetTrackedByAppointmentIdAsync(
            cancelledEvent.AppointmentId, cancellationToken);

        if (invoice is null)
        {
            await RecordProcessedAsync(cancelledEvent, AppointmentBillingResult.InvoiceNotFound, cancellationToken);
            _logger.LogWarning(
                "Cancellation message {MessageId} has no invoice for appointment {AppointmentId}.",
                cancelledEvent.MessageId,
                cancelledEvent.AppointmentId);
            return await SaveAsync(AppointmentBillingResult.InvoiceNotFound, cancellationToken);
        }

        if (!string.Equals(invoice.Status, InvoiceStatus.Draft, StringComparison.Ordinal))
        {
            await RecordProcessedAsync(cancelledEvent, AppointmentBillingResult.AlreadyFinalized, cancellationToken);
            _logger.LogWarning(
                "Invoice {InvoiceId} cannot be voided from status {Status}.",
                invoice.InvoiceId,
                invoice.Status);
            return await SaveAsync(AppointmentBillingResult.AlreadyFinalized, cancellationToken);
        }

        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        invoice.Status = InvoiceStatus.Void;
        invoice.VoidReason = cancelledEvent.Reason.Trim();
        invoice.VoidedAtUtc = EnsureUtc(cancelledEvent.OccurredAtUtc);
        invoice.UpdatedAtUtc = nowUtc;
        invoice.Version++;
        await RecordProcessedAsync(cancelledEvent, AppointmentBillingResult.Processed, cancellationToken);

        var result = await SaveAsync(AppointmentBillingResult.Processed, cancellationToken);
        _logger.LogInformation(
            "Voided invoice {InvoiceId} for cancelled appointment {AppointmentId}.",
            invoice.InvoiceId,
            invoice.AppointmentId);
        return result;
    }

    private async Task<AppointmentBillingResult> SaveAsync(
        AppointmentBillingResult intendedResult,
        CancellationToken cancellationToken)
    {
        try
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return intendedResult;
        }
        catch (DuplicateProcessedMessageException)
        {
            return AppointmentBillingResult.Duplicate;
        }
        catch (DuplicateInvoiceForAppointmentException)
        {
            return AppointmentBillingResult.InvoiceAlreadyExists;
        }
    }

    private Task RecordProcessedAsync(
        IntegrationEvent integrationEvent,
        AppointmentBillingResult outcome,
        CancellationToken cancellationToken) =>
        _processedMessageRepository.AddAsync(new ProcessedMessage
        {
            MessageId = integrationEvent.MessageId,
            EventType = integrationEvent.EventType,
            ConsumerGroup = ConsumerGroup,
            SourceTopic = SourceTopic,
            Outcome = outcome.ToString(),
            ProcessedAtUtc = _timeProvider.GetUtcNow().UtcDateTime
        }, cancellationToken);

    private static void ValidateBooked(AppointmentBookedEvent bookedEvent)
    {
        if (bookedEvent.MessageId == Guid.Empty) throw new ArgumentException("MessageId is required.");
        if (bookedEvent.AppointmentId == Guid.Empty) throw new ArgumentException("AppointmentId is required.");
        if (bookedEvent.PatientId == Guid.Empty) throw new ArgumentException("PatientId is required.");
        if (string.IsNullOrWhiteSpace(bookedEvent.ServiceCode)) throw new ArgumentException("ServiceCode is required.");
        if (bookedEvent.Version != 1) throw new ArgumentException("Only event version 1 is supported.");
    }

    private static void ValidateCompleted(AppointmentCompletedEvent completedEvent)
    {
        if (completedEvent.MessageId == Guid.Empty) throw new ArgumentException("MessageId is required.");
        if (completedEvent.AppointmentId == Guid.Empty) throw new ArgumentException("AppointmentId is required.");
        if (completedEvent.Version != 1) throw new ArgumentException("Only event version 1 is supported.");
    }

    private static void ValidateCancelled(AppointmentCancelledEvent cancelledEvent)
    {
        if (cancelledEvent.MessageId == Guid.Empty) throw new ArgumentException("MessageId is required.");
        if (cancelledEvent.AppointmentId == Guid.Empty) throw new ArgumentException("AppointmentId is required.");
        if (string.IsNullOrWhiteSpace(cancelledEvent.Reason)) throw new ArgumentException("Reason is required.");
        if (cancelledEvent.Reason.Trim().Length > 500) throw new ArgumentException("Reason must not exceed 500 characters.");
        if (cancelledEvent.Version != 1) throw new ArgumentException("Only event version 1 is supported.");
    }

    private static bool IsSpecializationCode(string serviceCode) =>
        serviceCode.StartsWith(SpecializationCodePrefix, StringComparison.Ordinal)
        && !string.Equals(serviceCode, DefaultSpecialistServiceCode, StringComparison.Ordinal);

    private static string CreateInvoiceNumber(Guid invoiceId, DateTime nowUtc) =>
        $"INV-{nowUtc:yyyyMMdd}-{invoiceId:N}"[..21].ToUpperInvariant();

    private static DateTime EnsureUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };
}
