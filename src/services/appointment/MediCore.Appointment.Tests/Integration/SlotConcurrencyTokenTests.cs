using MediCore.Appointment.Application.Entities;
using MediCore.Appointment.Application.Exceptions;
using MediCore.Appointment.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MediCore.Appointment.Tests.Integration;

/// <summary>
/// SCRUM-35 layer 1: the optimistic concurrency token on the slot row, proven against the real
/// database with no appointment involved — so it is the token, and not <c>ux_appointments_slot</c>,
/// that decides.
/// </summary>
/// <remarks>
/// Needs the local Postgres (<c>docker compose up -d postgres</c>). Each test writes one slot for a
/// fresh doctor id and deletes it afterwards.
/// </remarks>
[Trait("Category", "Integration")]
public sealed class SlotConcurrencyTokenTests : IClassFixture<AppointmentApiFactory>, IAsyncLifetime
{
    private readonly AppointmentApiFactory _factory;
    private readonly Guid _doctorId = Guid.NewGuid();
    private readonly Guid _slotId = Guid.NewGuid();

    public SlotConcurrencyTokenTests(AppointmentApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Of_two_writers_holding_the_same_read_only_the_first_commits()
    {
        using var first = _factory.Services.CreateScope();
        using var second = _factory.Services.CreateScope();

        // Both read the slot while it is Available — the moment every race starts from.
        var firstSlot = await LoadAsync(first);
        var secondSlot = await LoadAsync(second);

        firstSlot.Status = SlotStatus.Booked;
        await UnitOfWork(first).SaveChangesAsync();

        secondSlot.Status = SlotStatus.Blocked;
        await Assert.ThrowsAsync<ConcurrentUpdateException>(() => UnitOfWork(second).SaveChangesAsync());

        // The first writer's change stands; the second left no trace.
        Assert.Equal(SlotStatus.Booked, await ReadStatusAsync());
    }

    [Fact]
    public async Task A_stale_delete_is_refused_just_like_a_stale_update()
    {
        // Schedule reconciliation hard-deletes Available slots. Without the token, a slot booked
        // between reconciliation's read and its delete would vanish from under its appointment.
        using var reconciler = _factory.Services.CreateScope();
        using var booker = _factory.Services.CreateScope();

        var toDelete = await LoadAsync(reconciler);
        var toBook = await LoadAsync(booker);

        toBook.Status = SlotStatus.Booked;
        await UnitOfWork(booker).SaveChangesAsync();

        Db(reconciler).Slots.Remove(toDelete);
        await Assert.ThrowsAsync<ConcurrentUpdateException>(() => UnitOfWork(reconciler).SaveChangesAsync());

        Assert.Equal(SlotStatus.Booked, await ReadStatusAsync());
    }

    [Fact]
    public async Task After_a_conflict_the_same_context_rereads_and_can_save_again()
    {
        // What makes a retry possible: the unit of work clears the tracker, so the next read comes
        // from the database with the current token rather than reusing the stale entity.
        using var loser = _factory.Services.CreateScope();
        using var winner = _factory.Services.CreateScope();

        var stale = await LoadAsync(loser);
        var fresh = await LoadAsync(winner);

        fresh.UpdatedBy = "winner";
        await UnitOfWork(winner).SaveChangesAsync();

        stale.UpdatedBy = "loser";
        await Assert.ThrowsAsync<ConcurrentUpdateException>(() => UnitOfWork(loser).SaveChangesAsync());

        Assert.Empty(Db(loser).ChangeTracker.Entries());

        var reread = await LoadAsync(loser);
        Assert.Equal("winner", reread.UpdatedBy);
        reread.UpdatedBy = "loser, second attempt";
        await UnitOfWork(loser).SaveChangesAsync();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static AppointmentDbContext Db(IServiceScope scope) =>
        scope.ServiceProvider.GetRequiredService<AppointmentDbContext>();

    private static AppointmentUnitOfWork UnitOfWork(IServiceScope scope) => new(Db(scope));

    private Task<Slot> LoadAsync(IServiceScope scope) =>
        Db(scope).Slots.SingleAsync(slot => slot.SlotId == _slotId);

    private async Task<string> ReadStatusAsync()
    {
        using var scope = _factory.Services.CreateScope();
        return await Db(scope).Slots
            .AsNoTracking()
            .Where(slot => slot.SlotId == _slotId)
            .Select(slot => slot.Status)
            .SingleAsync();
    }

    /// <summary>One Available slot tomorrow, with no schedule — the token needs nothing else.</summary>
    public async Task InitializeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = Db(scope);
        var start = DateTime.UtcNow.Date.AddDays(1).AddHours(4);

        db.Slots.Add(new Slot
        {
            SlotId = _slotId,
            DoctorId = _doctorId,
            StartUtc = start,
            EndUtc = start.AddMinutes(15),
            SlotDate = DateOnly.FromDateTime(start),
            DurationMinutes = 15,
            Status = SlotStatus.Available,
            CreatedBy = "integration-test"
        });

        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        await Db(scope).Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM medicore_appointment.slots WHERE \"DoctorId\" = {_doctorId}");
    }
}
