using MediCore.Appointment.Application.DTOs;

namespace MediCore.Appointment.Application.Services;

/// <summary>
/// Which doctors can be booked, answered from the local doctor cache so that booking has no
/// runtime dependency on the Identity service.
/// </summary>
/// <remarks>
/// A doctor who is deactivated, has lost the Doctor role, or has not reached the cache yet is
/// absent from every answer here. Before the first backfill (<c>POST
/// /identity/api/staff/doctors/republish</c>) that means every doctor older than Kafka's retention.
/// </remarks>
public interface IDoctorDirectoryService
{
    /// <summary>
    /// Bookable doctors ordered by name, optionally narrowed to one specialization (whole name,
    /// case-insensitive).
    /// </summary>
    Task<IReadOnlyList<DoctorResponse>> ListBookableAsync(
        string? specialization,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The distinct specializations that currently have a bookable doctor, ordered. Empty ones are
    /// left out.
    /// </summary>
    Task<IReadOnlyList<string>> ListSpecializationsAsync(CancellationToken cancellationToken = default);

    /// <summary>The doctor if bookable; null when unknown or inactive.</summary>
    Task<DoctorResponse?> GetBookableAsync(
        Guid doctorId,
        CancellationToken cancellationToken = default);
}
