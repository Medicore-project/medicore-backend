using MediCore.Appointment.Application.DTOs;
using MediCore.Appointment.Application.Entities;
using MediCore.Appointment.Application.Interfaces;

namespace MediCore.Appointment.Application.Services;

/// <inheritdoc cref="IPublicHolidayService"/>
public sealed class PublicHolidayService : IPublicHolidayService
{
    private readonly IPublicHolidayRepository _repository;
    private readonly IScheduleRevisionService _revisionService;
    private readonly IUnitOfWork _unitOfWork;

    public PublicHolidayService(
        IPublicHolidayRepository repository,
        IScheduleRevisionService revisionService,
        IUnitOfWork unitOfWork)
    {
        _repository = repository;
        _revisionService = revisionService;
        _unitOfWork = unitOfWork;
    }

    public async Task<IReadOnlyList<PublicHolidayResponse>> GetAllAsync(
        CancellationToken cancellationToken = default)
    {
        var holidays = await _repository.GetAllAsync(cancellationToken);
        return holidays.Select(ToResponse).ToList();
    }

    public async Task<HolidayCreateResult> CreateAsync(
        CreatePublicHolidayRequest request,
        string actor,
        CancellationToken cancellationToken = default)
    {
        // Checked up front so a repeat declaration reads as a clear 409 rather than surfacing the
        // ux_public_holidays_date violation as an unhandled error.
        if (await _repository.ExistsOnDateAsync(request.Date, cancellationToken))
        {
            return new HolidayCreateDuplicateResult(request.Date);
        }

        var holiday = new PublicHoliday
        {
            Date = request.Date,
            Name = request.Name,
            CreatedBy = actor,
        };

        await _repository.AddAsync(holiday, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        var impact = await _revisionService.ReconcileAllDoctorsAsync(
            $"The clinic is closed on {request.Date:yyyy-MM-dd} ({request.Name}).",
            cancellationToken);

        return new HolidayCreatedResult(
            new PublicHolidayMutationResponse(ToResponse(holiday), impact));
    }

    public async Task<HolidayDeleteResult> DeleteAsync(
        Guid holidayId,
        string actor,
        CancellationToken cancellationToken = default)
    {
        var holiday = await _repository.GetTrackedByHolidayIdAsync(holidayId, cancellationToken);

        if (holiday is null)
        {
            return new HolidayDeleteNotFoundResult();
        }

        holiday.IsDeleted = true;
        holiday.UpdatedBy = actor;

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        var impact = await _revisionService.ReconcileAllDoctorsAsync(
            $"The closure on {holiday.Date:yyyy-MM-dd} was withdrawn.",
            cancellationToken);

        return new HolidayDeletedResult(impact);
    }

    private static PublicHolidayResponse ToResponse(PublicHoliday holiday) => new(
        holiday.HolidayId,
        holiday.Date,
        holiday.Name,
        holiday.CreatedAt,
        holiday.CreatedBy);
}
