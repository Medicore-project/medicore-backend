using MediCore.Appointment.Application.Exceptions;
using MediCore.Appointment.Application.Interfaces;

namespace MediCore.Appointment.UnitTests.Support;

/// <summary>Counts how many times the fake transaction was entered.</summary>
public sealed class TransactionProbe
{
    public int Attempts { get; internal set; }
}

public static class UnitOfWorkMockExtensions
{
    /// <summary>
    /// Turns <see cref="IUnitOfWork.ExecuteInTransactionAsync{T}"/> into a pass-through: Moq captures
    /// the <c>work</c> delegate the service hands over and simply invokes it, so the real
    /// read-decide-write logic still runs with no database underneath.
    /// </summary>
    /// <param name="failFirst">
    /// How many attempts should lose a concurrency race before the work is allowed to run. Each losing
    /// attempt faults with <see cref="ConcurrentUpdateException"/> — exactly what the real EF Core unit
    /// of work raises on a stale <c>xmin</c> token or a Postgres deadlock.
    /// </param>
    public static TransactionProbe SetupTransaction<T>(this Mock<IUnitOfWork> unitOfWork, int failFirst = 0)
    {
        var probe = new TransactionProbe();

        unitOfWork
            .Setup(u => u.ExecuteInTransactionAsync(
                It.IsAny<Func<CancellationToken, Task<T>>>(),
                It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<T>> work, CancellationToken token) =>
            {
                probe.Attempts++;
                return probe.Attempts <= failFirst
                    ? Task.FromException<T>(new ConcurrentUpdateException())
                    : work(token);
            });

        return probe;
    }
}
