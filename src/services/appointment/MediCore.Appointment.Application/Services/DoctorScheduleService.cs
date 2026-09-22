using MediCore.Appointment.Application.DTOs;
using MediCore.Appointment.Application.Entities;
using MediCore.Appointment.Application.Interfaces;
using MediCore.Appointment.Application.Scheduling;

namespace MediCore.Appointment.Application.Services;

/// <inheritdoc cref="IDoctorScheduleService"/>
public sealed class DoctorScheduleService : IDoctorScheduleService
{
    private readonly IDoctorScheduleRepository _repository;
    private readonly IDoctorCacheRepository _doctorRepository;
    private readonly IScheduleOverlapDetector _overlapDetector;
    private readonly IScheduleRevisionService _revisionService;
    private readonly IUnitOfWork _unitOfWork;

    public DoctorScheduleService(
        IDoctorScheduleRepository repository,
        IDoctorCacheRepository doctorRepository,
        IScheduleOverlapDetector overlapDetector,
        IScheduleRevisionService revisionService,
        IUnitOfWork unitOfWork)
    {
        _repository = repository;
        _doctorRepository = doctorRepository;
        _overlapDetector = overlapDetector;
        _revisionService = revisionService;
        _unitOfWork = unitOfWork;
    }

    public async Task<IReadOnlyList<DoctorScheduleResponse>> GetForDoctorAsync(
        Guid doctorId,
        CancellationToken cancellationToken = default)
    {
        var schedules = await _repository.GetAllForDoctorAsync(doctorId, cancellationToken);
        return schedules.Select(ToResponse).ToList();
    }

    public async Task<DoctorScheduleResponse?> GetByIdAsync(
        Guid scheduleId,
        CancellationToken cancellationToken = default)
    {
        var schedule = await _repository.GetByScheduleIdAsync(scheduleId, cancellationToken);
        return schedule is null ? null : ToResponse(schedule);
    }

    public async Task<ScheduleCreateResult> CreateAsync(
        CreateDoctorScheduleRequest request,
        string actor,
        CancellationToken cancellationToken = default)
    {
        // Checked against the local cache, never Identity, so scheduling works while Identity is
        // down. There is no FK to the cache either, which is why the check lives here.
        if (await _doctorRepository.GetActiveAsync(request.DoctorId, cancellationToken) is null)
        {
            return new ScheduleCreateDoctorNotFoundResult();
        }

        var schedule = new DoctorSchedule
        {
            DoctorId = request.DoctorId,
            DayOfWeek = request.DayOfWeek,
            StartTime = request.StartTime,
            EndTime = request.EndTime,
            SlotDurationMinutes = request.SlotDurationMinutes,
            EffectiveFrom = request.EffectiveFrom,
            EffectiveTo = request.EffectiveTo,
            IsActive = true,
            CreatedBy = actor,
        };

        var existing = await _repository.GetForOverlapCheckAsync(
            request.DoctorId, request.DayOfWeek, cancellationToken);

        if (_overlapDetector.FindConflict(schedule, existing) is { } conflict)
        {
            return new ScheduleCreateOverlapResult(conflict.ScheduleId, conflict.DayOfWeek);
        }

        await _repository.AddAsync(schedule, cancellationToken);

        // Committed before reconciling because reconciliation re-reads the doctor's schedules from
        // the database, where an un-saved insert would not yet be visible.
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        var impact = await _revisionService.ReconcileDoctorAsync(
            schedule.DoctorId, "A schedule was created.", cancellationToken);

        return new ScheduleCreatedResult(
            new DoctorScheduleMutationResponse(ToResponse(schedule), impact));
    }

    public async Task<ScheduleUpdateResult> UpdateAsync(
        Guid scheduleId,
        UpdateDoctorScheduleRequest request,
        string actor,
        CancellationToken cancellationToken = default)
    {
        var schedule = await _repository.GetTrackedByScheduleIdAsync(scheduleId, cancellationToken);

        if (schedule is null)
        {
            return new ScheduleUpdateNotFoundResult();
        }

        schedule.StartTime = request.StartTime;
        schedule.EndTime = request.EndTime;
        schedule.SlotDurationMinutes = request.SlotDurationMinutes;
        schedule.EffectiveFrom = request.EffectiveFrom;
        schedule.EffectiveTo = request.EffectiveTo;
        schedule.IsActive = request.IsActive;
        schedule.UpdatedBy = actor;

        // The detector skips the schedule being edited by matching on ScheduleId, so the proposed
        // values are checked against its siblings only.
        var siblings = await _repository.GetForOverlapCheckAsync(
            schedule.DoctorId, schedule.DayOfWeek, cancellationToken);

        if (_overlapDetector.FindConflict(schedule, siblings) is { } conflict)
        {
            return new ScheduleUpdateOverlapResult(conflict.ScheduleId, conflict.DayOfWeek);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        var impact = await _revisionService.ReconcileDoctorAsync(
            schedule.DoctorId, "The doctor's schedule was changed.", cancellationToken);

        return new ScheduleUpdatedResult(
            new DoctorScheduleMutationResponse(ToResponse(schedule), impact));
    }

    public async Task<ScheduleDeleteResult> DeleteAsync(
        Guid scheduleId,
        string actor,
        CancellationToken cancellationToken = default)
    {
        var schedule = await _repository.GetTrackedByScheduleIdAsync(scheduleId, cancellationToken);

        if (schedule is null)
        {
            return new ScheduleDeleteNotFoundResult();
        }

        // Soft delete: the global query filter then hides it, so reconciliation sees the doctor as
        // no longer working that window and clears the slots accordingly.
        schedule.IsDeleted = true;
        schedule.UpdatedBy = actor;

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        var impact = await _revisionService.ReconcileDoctorAsync(
            schedule.DoctorId, "The doctor's schedule was removed.", cancellationToken);

        return new ScheduleDeletedResult(impact);
    }

    public async Task<ScheduleRegenerateResult> RegenerateAsync(
        Guid scheduleId,
        CancellationToken cancellationToken = default)
    {
        var schedule = await _repository.GetByScheduleIdAsync(scheduleId, cancellationToken);

        if (schedule is null)
        {
            return new ScheduleRegenerateNotFoundResult();
        }

        var impact = await _revisionService.ReconcileDoctorAsync(
            schedule.DoctorId, "The doctor's schedule was regenerated.", cancellationToken);

        return new ScheduleRegeneratedResult(impact);
    }

    private static DoctorScheduleResponse ToResponse(DoctorSchedule schedule) => new(
        schedule.ScheduleId,
        schedule.DoctorId,
        schedule.DayOfWeek,
        schedule.StartTime,
        schedule.EndTime,
        schedule.SlotDurationMinutes,
        schedule.EffectiveFrom,
        schedule.EffectiveTo,
        schedule.IsActive,
        schedule.CreatedAt,
        schedule.CreatedBy);
}
