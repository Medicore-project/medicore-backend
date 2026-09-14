using MediCore.Contracts.Events.Appointment;

namespace MediCore.Patient.Application.Services;

public interface IAppointmentCompletedHandler
{
    Task<AppointmentCompletedHandlingResult> HandleAsync(
        AppointmentCompletedEvent completedEvent,
        CancellationToken cancellationToken = default);
}

public enum AppointmentCompletedHandlingResult
{
    Processed,
    Duplicate,
    DeadLetterQueued
}
