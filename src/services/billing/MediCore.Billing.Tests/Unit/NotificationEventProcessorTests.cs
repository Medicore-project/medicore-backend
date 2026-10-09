using System.Text.Json;
using MediCore.Billing.Application.Services;
using MediCore.Billing.Infrastructure.Messaging;
using MediCore.Contracts.Events.Appointment;
using MediCore.Contracts.Events.Billing;
using MediCore.Contracts.Events.Patient;

namespace MediCore.Billing.Tests.Unit;

public sealed class NotificationEventProcessorTests
{
    [Fact]
    public async Task Routes_all_three_supported_notification_events()
    {
        var service = new RecordingNotificationService();
        var processor = new NotificationEventProcessor(service);
        var patient = new PatientRegisteredEvent { PatientId = Guid.NewGuid(), FullName = "A", Email = "a@example.com" };
        var appointment = new AppointmentBookedEvent { AppointmentId = Guid.NewGuid(), PatientId = patient.PatientId, DoctorId = Guid.NewGuid(), SlotStart = DateTime.UtcNow, ServiceCode = "GEN-CONSULT" };
        var receipt = new InvoicePaidEvent { InvoiceId = Guid.NewGuid(), PatientId = patient.PatientId, Amount = 100m, Method = "Cash" };

        Assert.Equal(NotificationEventProcessingResult.Sent, await processor.ProcessAsync(patient.EventType, JsonSerializer.Serialize(patient), CancellationToken.None));
        Assert.Equal(NotificationEventProcessingResult.Sent, await processor.ProcessAsync(appointment.EventType, JsonSerializer.Serialize(appointment), CancellationToken.None));
        Assert.Equal(NotificationEventProcessingResult.Sent, await processor.ProcessAsync(receipt.EventType, JsonSerializer.Serialize(receipt), CancellationToken.None));

        Assert.Equal(1, service.PatientCalls);
        Assert.Equal(1, service.AppointmentCalls);
        Assert.Equal(1, service.ReceiptCalls);
    }

    [Fact]
    public async Task Unrelated_event_is_ignored()
    {
        var result = await new NotificationEventProcessor(new RecordingNotificationService())
            .ProcessAsync("appointment.cancelled", "{}", CancellationToken.None);

        Assert.Equal(NotificationEventProcessingResult.Ignored, result);
    }

    private sealed class RecordingNotificationService : INotificationService
    {
        public int PatientCalls { get; private set; }
        public int AppointmentCalls { get; private set; }
        public int ReceiptCalls { get; private set; }
        public Task<NotificationDeliveryResult> HandlePatientRegisteredAsync(PatientRegisteredEvent integrationEvent, CancellationToken cancellationToken = default) { PatientCalls++; return Task.FromResult(NotificationDeliveryResult.Sent); }
        public Task<NotificationDeliveryResult> HandleAppointmentBookedAsync(AppointmentBookedEvent integrationEvent, CancellationToken cancellationToken = default) { AppointmentCalls++; return Task.FromResult(NotificationDeliveryResult.Sent); }
        public Task<NotificationDeliveryResult> HandleInvoicePaidAsync(InvoicePaidEvent integrationEvent, CancellationToken cancellationToken = default) { ReceiptCalls++; return Task.FromResult(NotificationDeliveryResult.Sent); }
    }
}
