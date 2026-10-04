using MediCore.Contracts.Events.Appointment;

namespace MediCore.Billing.Application.Services;

public interface IAppointmentBillingHandler
{
    Task<AppointmentBillingResult> HandleBookedAsync(
        AppointmentBookedEvent bookedEvent,
        CancellationToken cancellationToken = default);

    Task<AppointmentBillingResult> HandleCompletedAsync(
        AppointmentCompletedEvent completedEvent,
        CancellationToken cancellationToken = default);
}

public enum AppointmentBillingResult
{
    Processed,
    MissingTariff,
    Duplicate,
    InvoiceAlreadyExists,
    InvoiceNotFound,
    AlreadyFinalized
}
