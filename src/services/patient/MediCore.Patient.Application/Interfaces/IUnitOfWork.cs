namespace MediCore.Patient.Application.Interfaces;

public interface IUnitOfWork
{
    Task SaveChangesAsync(string duplicateNic, CancellationToken cancellationToken = default);
}
