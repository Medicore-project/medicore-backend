namespace MediCore.Appointment.Application.Interfaces;

/// <summary>
/// Commits the work staged by the repositories. Repositories never call <c>SaveChanges</c>
/// themselves, so one HTTP request writes in a single transaction.
/// </summary>
public interface IUnitOfWork
{
    /// <summary>
    /// Commits staged changes, translating database constraint violations into domain exceptions.
    /// </summary>
    /// <exception cref="Exceptions.DuplicateSlotException">
    /// A slot already exists for that doctor and instant — two reconciliations of the same doctor
    /// raced each other.
    /// </exception>
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
