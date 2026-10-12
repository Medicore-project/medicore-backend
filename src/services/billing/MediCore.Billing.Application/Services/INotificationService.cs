using MediCore.Contracts.Events.Appointment;
using MediCore.Contracts.Events.Billing;
using MediCore.Contracts.Events.Patient;

namespace MediCore.Billing.Application.Services;

public interface INotificationService
{
    Task<NotificationDeliveryResult> HandlePatientRegisteredAsync(PatientRegisteredEvent integrationEvent, CancellationToken cancellationToken = default);
    Task<NotificationDeliveryResult> HandleAppointmentBookedAsync(AppointmentBookedEvent integrationEvent, CancellationToken cancellationToken = default);
    Task<NotificationDeliveryResult> HandleInvoicePaidAsync(InvoicePaidEvent integrationEvent, CancellationToken cancellationToken = default);
}

public enum NotificationDeliveryResult
{
    Sent,
    Duplicate
}
