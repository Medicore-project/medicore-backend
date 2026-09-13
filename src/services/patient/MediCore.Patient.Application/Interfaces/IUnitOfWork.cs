namespace MediCore.Patient.Application.Interfaces;

public interface IUnitOfWork
{
    Task SaveChangesAsync(CancellationToken cancellationToken = default);

    Task SaveRegistrationAsync(string duplicateNic, CancellationToken cancellationToken = default);
}
