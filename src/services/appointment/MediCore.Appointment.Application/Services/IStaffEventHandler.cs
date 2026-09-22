using MediCore.Contracts.Events.Staff;

namespace MediCore.Appointment.Application.Services;

// ── Discriminated union results ───────────────────────────────────────────────

/// <summary>
/// What handling one staff event concluded. Every variant means the message is finished with and
/// its offset may be committed; a failure that should be retried is thrown, not returned.
/// </summary>
public abstract record StaffEventResult;

/// <summary>The doctor cache changed.</summary>
public sealed record StaffEventAppliedResult : StaffEventResult;

/// <summary>This message was already handled — a redelivery. Nothing was written.</summary>
public sealed record StaffEventDuplicateResult : StaffEventResult;

/// <summary>
/// The cached row already reflects a newer event, so this older one was recorded and skipped.
/// </summary>
public sealed record StaffEventStaleResult : StaffEventResult;

/// <summary>Valid, but nothing for this service to do — for example a nurse being created.</summary>
public sealed record StaffEventIgnoredResult(string Reason) : StaffEventResult;

/// <summary>
/// Malformed or unsupported. Logged and skipped so that it cannot block the partition; there is no
/// dead-letter topic publishing in this service until it gains a Kafka producer.
/// </summary>
public sealed record StaffEventRejectedResult(string Reason) : StaffEventResult;

// ── Handler contract ──────────────────────────────────────────────────────────

/// <summary>
/// Applies Identity's staff-events to this service's <see cref="Entities.DoctorCache"/>.
/// </summary>
/// <remarks>
/// Idempotent: each message is recorded in the processed-message log in the same transaction as its
/// effect, so a redelivery is recognised and skipped. An event older than the cached row is recorded
/// but not applied, so a replay can never roll a doctor back to an earlier state.
/// </remarks>
public interface IStaffEventHandler
{
    Task<StaffEventResult> HandleCreatedAsync(
        StaffCreatedEvent createdEvent,
        CancellationToken cancellationToken = default);

    Task<StaffEventResult> HandleUpdatedAsync(
        StaffUpdatedEvent updatedEvent,
        CancellationToken cancellationToken = default);

    Task<StaffEventResult> HandleDeactivatedAsync(
        StaffDeactivatedEvent deactivatedEvent,
        CancellationToken cancellationToken = default);
}
