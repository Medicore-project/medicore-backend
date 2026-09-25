using MediCore.Appointment.Application.Concurrency;
using MediCore.Appointment.Application.Exceptions;

namespace MediCore.Appointment.Tests.Unit;

public sealed class ConcurrencyRetryTests
{
    [Fact]
    public async Task An_operation_that_succeeds_runs_once()
    {
        var calls = 0;

        var result = await ConcurrencyRetry.RunAsync(_ =>
        {
            calls++;
            return Task.FromResult("done");
        });

        Assert.Equal("done", result);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task A_lost_race_runs_the_whole_operation_again()
    {
        var calls = 0;

        var result = await ConcurrencyRetry.RunAsync(_ =>
        {
            calls++;
            return calls == 1
                ? throw new ConcurrentUpdateException()
                : Task.FromResult($"attempt {calls}");
        });

        Assert.Equal("attempt 2", result);
    }

    [Fact]
    public async Task After_three_lost_races_the_conflict_escapes()
    {
        // Escaping is the right end: the API answers it with 409, and a fourth try would only hide
        // a writer that is hammering this row.
        var calls = 0;

        await Assert.ThrowsAsync<ConcurrentUpdateException>(() =>
            ConcurrencyRetry.RunAsync<string>(_ =>
            {
                calls++;
                throw new ConcurrentUpdateException();
            }));

        Assert.Equal(ConcurrencyRetry.MaxAttempts, calls);
        Assert.Equal(3, calls);
    }

    [Fact]
    public async Task Losing_to_a_unique_index_is_not_retried()
    {
        // A unique violation is an answer, not a stale read: the slot is taken, and asking again
        // cannot change that.
        var calls = 0;

        await Assert.ThrowsAsync<SlotAlreadyBookedException>(() =>
            ConcurrencyRetry.RunAsync<string>(_ =>
            {
                calls++;
                throw new SlotAlreadyBookedException();
            }));

        Assert.Equal(1, calls);
    }
}
