using MediCore.Appointment.Application.Entities;
using MediCore.Appointment.Application.Exceptions;
using MediCore.Appointment.Application.Interfaces;
using MediCore.Contracts.Events;
using MediCore.Contracts.Events.Staff;
using Microsoft.Extensions.Logging;

namespace MediCore.Appointment.Application.Services;

/// <inheritdoc cref="IStaffEventHandler"/>
public sealed class StaffEventHandler : IStaffEventHandler
{
    public const string ConsumerGroup = "medicore-appointment";
    public const string SourceTopic = "staff-events";
    public const string DoctorRole = "Doctor";

    /// <summary>Written to CreatedBy/UpdatedBy so cache rows show they came from events, not a user.</summary>
    public const string IntegrationActor = "staff-events";

    // Column limits on cached_doctors. A longer value would fail the insert on every retry and
    // wedge the partition, so it is rejected up front instead.
    private const int FullNameMaxLength = 200;
    private const int SpecializationMaxLength = 100;

    private readonly IDoctorCacheRepository _doctorRepository;
    private readonly IProcessedMessageRepository _processedMessageRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<StaffEventHandler> _logger;

    public StaffEventHandler(
        IDoctorCacheRepository doctorRepository,
        IProcessedMessageRepository processedMessageRepository,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider,
        ILogger<StaffEventHandler> logger)
    {
        _doctorRepository = doctorRepository;
        _processedMessageRepository = processedMessageRepository;
        _unitOfWork = unitOfWork;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public Task<StaffEventResult> HandleCreatedAsync(
        StaffCreatedEvent createdEvent,
        CancellationToken cancellationToken = default) =>
        HandleAsync(
            createdEvent,
            createdEvent.StaffId,
            ValidateProfile(createdEvent.FullName, createdEvent.Specialization),
            async (doctor, occurredAtUtc) =>
            {
                if (!IsDoctor(createdEvent.Role))
                {
                    return new StaffEventIgnoredResult("NotADoctor");
                }

                await UpsertAsync(
                    doctor,
                    createdEvent.StaffId,
                    createdEvent.FullName,
                    createdEvent.Specialization,
                    createdEvent.DepartmentId,
                    isActive: true,
                    occurredAtUtc,
                    cancellationToken);
                return new StaffEventAppliedResult();
            },
            cancellationToken);

    public Task<StaffEventResult> HandleUpdatedAsync(
        StaffUpdatedEvent updatedEvent,
        CancellationToken cancellationToken = default) =>
        HandleAsync(
            updatedEvent,
            updatedEvent.StaffId,
            ValidateProfile(updatedEvent.FullName, updatedEvent.Specialization),
            async (doctor, occurredAtUtc) =>
            {
                // Published before Role existed on the event. It cannot say whether this person is
                // a doctor, so it may only refresh the details of a doctor already cached.
                if (updatedEvent.Role is null)
                {
                    if (doctor is null)
                    {
                        return new StaffEventIgnoredResult("UnknownDoctor");
                    }

                    ApplyProfile(
                        doctor,
                        updatedEvent.FullName,
                        updatedEvent.Specialization,
                        updatedEvent.DepartmentId,
                        updatedEvent.IsActive ?? doctor.IsActive,
                        occurredAtUtc);
                    return new StaffEventAppliedResult();
                }

                if (!IsDoctor(updatedEvent.Role))
                {
                    if (doctor is null)
                    {
                        return new StaffEventIgnoredResult("NotADoctor");
                    }

                    // The doctor role was taken away. Keep the row so existing slots still show a
                    // name, but stop it being bookable.
                    MarkInactive(doctor, occurredAtUtc);
                    return new StaffEventAppliedResult();
                }

                // Full state, so this is an upsert: it is also how the backfill fills an empty cache.
                await UpsertAsync(
                    doctor,
                    updatedEvent.StaffId,
                    updatedEvent.FullName,
                    updatedEvent.Specialization,
                    updatedEvent.DepartmentId,
                    updatedEvent.IsActive ?? doctor?.IsActive ?? true,
                    occurredAtUtc,
                    cancellationToken);
                return new StaffEventAppliedResult();
            },
            cancellationToken);

    public Task<StaffEventResult> HandleDeactivatedAsync(
        StaffDeactivatedEvent deactivatedEvent,
        CancellationToken cancellationToken = default) =>
        HandleAsync(
            deactivatedEvent,
            deactivatedEvent.StaffId,
            invalidReason: null,
            (doctor, occurredAtUtc) =>
            {
                if (doctor is null)
                {
                    // Not a doctor, or one this cache never heard of. Either way nothing is bookable.
                    return Task.FromResult<StaffEventResult>(new StaffEventIgnoredResult("UnknownDoctor"));
                }

                MarkInactive(doctor, occurredAtUtc);
                return Task.FromResult<StaffEventResult>(new StaffEventAppliedResult());
            },
            cancellationToken);

    /// <summary>
    /// The steps every staff event goes through: dedupe, validate, stale check, apply, then record
    /// the outcome and commit both in one save.
    /// </summary>
    private async Task<StaffEventResult> HandleAsync(
        IntegrationEvent staffEvent,
        Guid staffId,
        string? invalidReason,
        Func<DoctorCache?, DateTime, Task<StaffEventResult>> apply,
        CancellationToken cancellationToken)
    {
        if (staffEvent.MessageId == Guid.Empty)
        {
            // Without a MessageId it cannot be recorded — every such message would share one key.
            _logger.LogWarning(
                "Rejecting {EventType} for staff {StaffId}: it has no MessageId.",
                staffEvent.EventType,
                staffId);
            return new StaffEventRejectedResult("MissingMessageId");
        }

        if (await _processedMessageRepository.ExistsAsync(staffEvent.MessageId, cancellationToken))
        {
            _logger.LogInformation(
                "Skipping duplicate {EventType} message {MessageId}.",
                staffEvent.EventType,
                staffEvent.MessageId);
            return new StaffEventDuplicateResult();
        }

        var rejectReason = staffId == Guid.Empty ? "MissingStaffId"
            : staffEvent.Version != 1 ? "UnsupportedEventVersion"
            : invalidReason;
        if (rejectReason is not null)
        {
            _logger.LogWarning(
                "Rejecting {EventType} message {MessageId} for staff {StaffId}. Reason: {RejectReason}.",
                staffEvent.EventType,
                staffEvent.MessageId,
                staffId,
                rejectReason);
            return await RecordAsync(staffEvent, new StaffEventRejectedResult(rejectReason), cancellationToken);
        }

        var occurredAtUtc = EnsureUtc(staffEvent.OccurredAtUtc);
        var doctor = await _doctorRepository.GetTrackedByDoctorIdAsync(staffId, cancellationToken);

        // Equal timestamps are applied, not skipped: the event carries full state, so applying it
        // again is harmless, and skipping could drop a change made in the same instant.
        if (doctor is not null && occurredAtUtc < doctor.LastEventOccurredAtUtc)
        {
            _logger.LogInformation(
                "Skipping stale {EventType} message {MessageId} for doctor {DoctorId}: occurred {OccurredAtUtc:O}, cache is at {LastEventOccurredAtUtc:O}.",
                staffEvent.EventType,
                staffEvent.MessageId,
                staffId,
                occurredAtUtc,
                doctor.LastEventOccurredAtUtc);
            return await RecordAsync(staffEvent, new StaffEventStaleResult(), cancellationToken);
        }

        var result = await apply(doctor, occurredAtUtc);
        return await RecordAsync(staffEvent, result, cancellationToken);
    }

    private async Task<StaffEventResult> RecordAsync(
        IntegrationEvent staffEvent,
        StaffEventResult result,
        CancellationToken cancellationToken)
    {
        await _processedMessageRepository.AddAsync(new ProcessedMessage
        {
            MessageId = staffEvent.MessageId,
            EventType = staffEvent.EventType,
            ConsumerGroup = ConsumerGroup,
            SourceTopic = SourceTopic,
            Outcome = ToOutcome(result),
            ProcessedAtUtc = _timeProvider.GetUtcNow().UtcDateTime
        }, cancellationToken);

        try
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return result;
        }
        catch (DuplicateProcessedMessageException)
        {
            // Another attempt at the same message committed first; its effect is already saved.
            return new StaffEventDuplicateResult();
        }
    }

    private async Task UpsertAsync(
        DoctorCache? doctor,
        Guid staffId,
        string fullName,
        string? specialization,
        Guid departmentId,
        bool isActive,
        DateTime occurredAtUtc,
        CancellationToken cancellationToken)
    {
        if (doctor is null)
        {
            doctor = new DoctorCache
            {
                DoctorId = staffId,
                CreatedBy = IntegrationActor
            };
            await _doctorRepository.AddAsync(doctor, cancellationToken);
            SetProfile(doctor, fullName, specialization, departmentId, isActive, occurredAtUtc);
            return;
        }

        ApplyProfile(doctor, fullName, specialization, departmentId, isActive, occurredAtUtc);
    }

    /// <summary>Updates an existing row.</summary>
    private static void ApplyProfile(
        DoctorCache doctor,
        string fullName,
        string? specialization,
        Guid departmentId,
        bool isActive,
        DateTime occurredAtUtc)
    {
        SetProfile(doctor, fullName, specialization, departmentId, isActive, occurredAtUtc);

        // Set explicitly: the DbContext only fills UpdatedBy when it is null, so a previous
        // writer's name would otherwise survive this change.
        doctor.UpdatedBy = IntegrationActor;
    }

    private static void SetProfile(
        DoctorCache doctor,
        string fullName,
        string? specialization,
        Guid departmentId,
        bool isActive,
        DateTime occurredAtUtc)
    {
        doctor.FullName = fullName.Trim();
        doctor.Specialization = specialization?.Trim() ?? string.Empty;
        doctor.DepartmentId = departmentId;
        doctor.IsActive = isActive;
        doctor.LastEventOccurredAtUtc = occurredAtUtc;
    }

    private static void MarkInactive(DoctorCache doctor, DateTime occurredAtUtc)
    {
        doctor.IsActive = false;
        doctor.LastEventOccurredAtUtc = occurredAtUtc;
        doctor.UpdatedBy = IntegrationActor;
    }

    private static string? ValidateProfile(string? fullName, string? specialization)
    {
        if (string.IsNullOrWhiteSpace(fullName)) return "MissingFullName";
        if (fullName.Trim().Length > FullNameMaxLength) return "FullNameTooLong";
        if ((specialization?.Trim().Length ?? 0) > SpecializationMaxLength) return "SpecializationTooLong";
        return null;
    }

    private static bool IsDoctor(string? role) =>
        string.Equals(role?.Trim(), DoctorRole, StringComparison.OrdinalIgnoreCase);

    private static string ToOutcome(StaffEventResult result) => result switch
    {
        StaffEventAppliedResult => ProcessedMessageOutcome.Applied,
        StaffEventIgnoredResult => ProcessedMessageOutcome.Ignored,
        StaffEventStaleResult => ProcessedMessageOutcome.Stale,
        StaffEventRejectedResult => ProcessedMessageOutcome.Rejected,
        _ => throw new InvalidOperationException($"Result '{result}' is not recorded as an outcome.")
    };

    private static DateTime EnsureUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };
}
