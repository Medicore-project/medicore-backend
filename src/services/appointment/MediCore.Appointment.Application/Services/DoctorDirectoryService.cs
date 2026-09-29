using MediCore.Appointment.Application.DTOs;
using MediCore.Appointment.Application.Entities;
using MediCore.Appointment.Application.Interfaces;

namespace MediCore.Appointment.Application.Services;

/// <inheritdoc cref="IDoctorDirectoryService"/>
public sealed class DoctorDirectoryService : IDoctorDirectoryService
{
    private readonly IDoctorCacheRepository _repository;

    public DoctorDirectoryService(IDoctorCacheRepository repository)
    {
        _repository = repository;
    }

    public async Task<IReadOnlyList<DoctorResponse>> ListBookableAsync(
        string? specialization,
        CancellationToken cancellationToken = default)
    {
        var doctors = await _repository.ListActiveAsync(specialization, cancellationToken);
        return doctors.Select(ToResponse).ToList();
    }

    public Task<IReadOnlyList<string>> ListSpecializationsAsync(
        CancellationToken cancellationToken = default) =>
        _repository.ListSpecializationsAsync(cancellationToken);

    public async Task<DoctorResponse?> GetBookableAsync(
        Guid doctorId,
        CancellationToken cancellationToken = default)
    {
        var doctor = await _repository.GetActiveAsync(doctorId, cancellationToken);
        return doctor is null ? null : ToResponse(doctor);
    }

    private static DoctorResponse ToResponse(DoctorCache doctor) => new(
        doctor.DoctorId,
        doctor.FullName,
        doctor.Specialization,
        doctor.DepartmentId);
}
