using MediCore.Appointment.Application.DTOs;
using MediCore.Appointment.Application.Validators;

namespace MediCore.Appointment.Tests.Unit;

public sealed class ScheduleValidatorTests
{
    private readonly CreateDoctorScheduleRequestValidator _createValidator = new();
    private readonly UpdateDoctorScheduleRequestValidator _updateValidator = new();

    [Theory]
    [InlineData(15)]
    [InlineData(30)]
    public void Create_accepts_supported_slot_durations(int duration)
    {
        var result = _createValidator.Validate(CreateRequest(duration));

        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(20)]
    [InlineData(45)]
    [InlineData(60)]
    public void Create_rejects_unsupported_slot_durations(int duration)
    {
        var result = _createValidator.Validate(CreateRequest(duration));

        Assert.Contains(result.Errors, error => error.PropertyName == nameof(CreateDoctorScheduleRequest.SlotDurationMinutes));
    }

    [Fact]
    public void Create_rejects_an_empty_doctor_or_invalid_time_and_effective_ranges()
    {
        var request = new CreateDoctorScheduleRequest(
            Guid.Empty,
            DayOfWeek.Monday,
            new TimeOnly(10, 0),
            new TimeOnly(9, 0),
            30,
            new DateOnly(2026, 10, 1),
            new DateOnly(2026, 9, 30));

        var result = _createValidator.Validate(request);

        Assert.Contains(result.Errors, error => error.PropertyName == nameof(CreateDoctorScheduleRequest.DoctorId));
        Assert.Contains(result.Errors, error => error.PropertyName == nameof(CreateDoctorScheduleRequest.EndTime));
        Assert.Contains(result.Errors, error => error.PropertyName == nameof(CreateDoctorScheduleRequest.EffectiveTo));
    }

    [Fact]
    public void Update_rejects_a_window_shorter_than_one_slot()
    {
        var request = new UpdateDoctorScheduleRequest(
            new TimeOnly(9, 0),
            new TimeOnly(9, 15),
            30,
            new DateOnly(2026, 9, 21),
            null,
            true);

        var result = _updateValidator.Validate(request);

        Assert.Contains(result.Errors, error => error.PropertyName == nameof(UpdateDoctorScheduleRequest.EndTime));
    }

    private static CreateDoctorScheduleRequest CreateRequest(int duration) => new(
        Guid.NewGuid(),
        DayOfWeek.Monday,
        new TimeOnly(9, 0),
        new TimeOnly(10, 0),
        duration,
        new DateOnly(2026, 9, 21),
        null);
}
