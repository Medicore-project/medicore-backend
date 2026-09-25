using MediCore.Appointment.Application.Entities;

namespace MediCore.Appointment.Application.Interfaces;

/// <summary>Data-access contract for <see cref="DoctorCache"/>.</summary>
public interface IDoctorCacheRepository
{
    /// <summary>
    /// Returns a change-tracked row by doctor id whether active or not, for the staff-events
    /// consumer to update.
    /// </summary>
    Task<DoctorCache?> GetTrackedByDoctorIdAsync(Guid doctorId, CancellationToken cancellationToken = default);

    /// <summary>The doctor if cached and bookable; null if unknown or inactive.</summary>
    Task<DoctorCache?> GetActiveAsync(Guid doctorId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Every bookable doctor ordered by name, optionally narrowed to one specialization
    /// (whole name, case-insensitive).
    /// </summary>
    Task<IReadOnlyList<DoctorCache>> ListActiveAsync(
        string? specialization,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The distinct specializations of bookable doctors, ordered, with blanks left out.
    /// </summary>
    /// <remarks>
    /// Specialization is free text in Identity and may be empty, so an unset one must not appear
    /// as a nameless choice on the public booking page.
    /// </remarks>
    Task<IReadOnlyList<string>> ListSpecializationsAsync(CancellationToken cancellationToken = default);

    /// <summary>Stages a new row for insertion (not yet committed).</summary>
    Task AddAsync(DoctorCache doctor, CancellationToken cancellationToken = default);
}
