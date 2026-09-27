using MediCore.Appointment.Application.Entities;

namespace MediCore.Appointment.Application.Interfaces;

/// <summary>Data-access contract for <see cref="AppointmentHistoryEntry"/>. Append and read, nothing else.</summary>
public interface IAppointmentHistoryRepository
{
    /// <summary>Stages an entry for insertion; it commits with the change it describes.</summary>
    Task AddAsync(AppointmentHistoryEntry entry, CancellationToken cancellationToken = default);

    /// <summary>Every entry for one appointment, oldest first.</summary>
    Task<IReadOnlyList<AppointmentHistoryEntry>> ListForAppointmentAsync(
        Guid appointmentId,
        CancellationToken cancellationToken = default);
}
