using System.Text.Json;
using MediCore.Billing.Application.Services;
using MediCore.Billing.Infrastructure.Messaging;
using MediCore.Contracts.Events.Appointment;

namespace MediCore.Billing.Tests.Unit;

public sealed class AppointmentEventProcessorTests
{
    [Fact]
    public async Task Cancelled_event_is_routed_to_the_billing_handler()
    {
        var handler = new RecordingHandler();
        var processor = new AppointmentEventProcessor(handler);
        var cancelledEvent = new AppointmentCancelledEvent
        {
            MessageId = Guid.NewGuid(),
            AppointmentId = Guid.NewGuid(),
            Reason = "Patient request",
            CorrelationId = "corr-43",
            OccurredAtUtc = new DateTime(2026, 10, 6, 8, 0, 0, DateTimeKind.Utc)
        };

        var result = await processor.ProcessAsync(
            cancelledEvent.EventType,
            JsonSerializer.Serialize(cancelledEvent),
            CancellationToken.None);

        Assert.Equal(AppointmentEventProcessingResult.Processed, result);
        Assert.Equal(cancelledEvent, handler.CancelledEvent);
    }

    private sealed class RecordingHandler : IAppointmentBillingHandler
    {
        public AppointmentCancelledEvent? CancelledEvent { get; private set; }

        public Task<AppointmentBillingResult> HandleBookedAsync(
            AppointmentBookedEvent bookedEvent,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(AppointmentBillingResult.Processed);

        public Task<AppointmentBillingResult> HandleCompletedAsync(
            AppointmentCompletedEvent completedEvent,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(AppointmentBillingResult.Processed);

        public Task<AppointmentBillingResult> HandleCancelledAsync(
            AppointmentCancelledEvent cancelledEvent,
            CancellationToken cancellationToken = default)
        {
            CancelledEvent = cancelledEvent;
            return Task.FromResult(AppointmentBillingResult.Processed);
        }
    }
}
