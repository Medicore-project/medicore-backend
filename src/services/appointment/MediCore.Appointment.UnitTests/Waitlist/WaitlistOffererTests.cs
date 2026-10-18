using MediCore.Appointment.Application.Entities;
using MediCore.Appointment.Application.Interfaces;
using MediCore.Appointment.Application.Scheduling;
using MediCore.Appointment.Application.Services;
using MediCore.Appointment.UnitTests.Support;
using Microsoft.Extensions.Options;

namespace MediCore.Appointment.UnitTests.Waitlist;

/// <summary>
/// SCRUM-37 — offering a released slot to the first suitable patient in the waitlist queue.
/// </summary>
[Trait(TestCategories.Category, TestCategories.Unit)]
[Trait(TestCategories.Feature, TestCategories.Waitlist)]
public sealed class WaitlistOffererTests
{
    private const string Actor = "reception@medicore.test";

    private readonly Mock<IWaitlistRepository> _waitlist = new();
    private readonly Mock<IAppointmentRepository> _appointments = new();
    private readonly Mock<IDoctorCacheRepository> _doctors = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();

    public WaitlistOffererTests()
    {
        _doctors
            .Setup(d => d.GetActiveAsync(Ids.Doctor, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Doctors.Active());
    }

    private WaitlistOfferer CreateOfferer(int offerWindowMinutes = 60, int minimumLeadMinutes = 15) => new(
        _waitlist.Object,
        _appointments.Object,
        _doctors.Object,
        _unitOfWork.Object,
        Options.Create(new WaitlistOptions
        {
            OfferWindowMinutes = offerWindowMinutes,
            MinimumLeadMinutes = minimumLeadMinutes
        }));

    private static WaitlistEntry Waiting(int position, string status = WaitlistStatus.Waiting) => new()
    {
        DoctorId = Ids.Doctor,
        SlotDate = DateOnly.FromDateTime(TestClock.Now.AddDays(1)),
        PatientId = Guid.NewGuid(),
        Position = position,
        Status = status
    };

    private void GivenQueue(params WaitlistEntry[] entries) =>
        _waitlist
            .Setup(w => w.GetWaitingTrackedAsync(Ids.Doctor, It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(entries);

    // ── Pre-conditions ───────────────────────────────────────────────────────

    [Theory]
    [InlineData(SlotStatus.Booked)]
    [InlineData(SlotStatus.Offered)]
    [Trait(TestCategories.Technique, TestCategories.Parameterized)]
    public async Task Only_an_Available_slot_is_ever_offered(string status)
    {
        // STRICT mocks with no setups: if the offerer touches any dependency at all, Moq throws.
        var offerer = new WaitlistOfferer(
            new Mock<IWaitlistRepository>(MockBehavior.Strict).Object,
            new Mock<IAppointmentRepository>(MockBehavior.Strict).Object,
            new Mock<IDoctorCacheRepository>(MockBehavior.Strict).Object,
            new Mock<IUnitOfWork>(MockBehavior.Strict).Object,
            Options.Create(new WaitlistOptions()));
        var slot = SlotBuilder.ASlot().WithStatus(status).Build();

        var offered = await offerer.OfferAsync(slot, TestClock.Now, Actor);

        Assert.Null(offered);
        Assert.Equal(status, slot.Status);
    }

    [Theory]
    [InlineData(15, 14, false)]  // too soon for anyone to get there
    [InlineData(15, 15, true)]   // boundary: exactly the minimum lead
    [Trait(TestCategories.Technique, TestCategories.Parameterized)]
    public async Task A_slot_starting_too_soon_is_not_offered(int minimumLeadMinutes, int minutesUntilStart, bool offered)
    {
        GivenQueue(Waiting(1));
        var slot = SlotBuilder.ASlot().StartingIn(TimeSpan.FromMinutes(minutesUntilStart)).Build();

        var entry = await CreateOfferer(minimumLeadMinutes: minimumLeadMinutes).OfferAsync(slot, TestClock.Now, Actor);

        Assert.Equal(offered, entry is not null);
        Assert.Equal(offered ? SlotStatus.Offered : SlotStatus.Available, slot.Status);
        _waitlist.Verify(
            w => w.LockQueueAsync(It.IsAny<Guid>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()),
            offered ? Times.Once() : Times.Never());
    }

    [Fact]
    [Trait(TestCategories.Technique, TestCategories.Mocking)]
    public async Task Nothing_is_offered_for_a_doctor_who_has_left()
    {
        _doctors
            .Setup(d => d.GetActiveAsync(Ids.Doctor, It.IsAny<CancellationToken>()))
            .ReturnsAsync((DoctorCache?)null);

        var entry = await CreateOfferer().OfferAsync(SlotBuilder.ASlot().Build(), TestClock.Now, Actor);

        Assert.Null(entry);
        _waitlist.VerifyNoOtherCalls();
    }

    // ── Walking the queue ────────────────────────────────────────────────────

    [Fact]
    [Trait(TestCategories.Technique, TestCategories.Mocking)]
    public async Task The_queue_is_locked_then_walked_skipping_unsuitable_patients_until_one_fits()
    {
        var slot = SlotBuilder.ASlot().StartingIn(TimeSpan.FromHours(4)).Build();
        var alreadyBooked = Waiting(1);
        var alreadyOffered = Waiting(2, WaitlistStatus.Offered);
        var busyThen = Waiting(3);
        var winner = Waiting(4);
        var neverReached = Waiting(5);

        var calls = new List<string>();
        _waitlist
            .Setup(w => w.LockQueueAsync(Ids.Doctor, slot.SlotDate, It.IsAny<CancellationToken>()))
            .Callback(() => calls.Add("lock"))
            .Returns(Task.CompletedTask);
        _waitlist
            .Setup(w => w.GetWaitingTrackedAsync(Ids.Doctor, slot.SlotDate, It.IsAny<CancellationToken>()))
            .Callback(() => calls.Add("read-queue"))
            .ReturnsAsync(new[] { alreadyBooked, alreadyOffered, busyThen, winner, neverReached });

        // Patient #1 already has a booking with this doctor that day -> withdrawn.
        _waitlist
            .Setup(w => w.HasBookedWithDoctorOnAsync(alreadyBooked.PatientId, Ids.Doctor, slot.SlotDate, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        // Patient #3 is booked elsewhere at an overlapping time -> skipped but keeps their place.
        _appointments
            .Setup(a => a.FindPatientOverlapAsync(busyThen.PatientId, slot.StartUtc, slot.EndUtc, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(AppointmentBuilder.AnAppointment().Build());

        var offered = await CreateOfferer().OfferAsync(slot, TestClock.Now, Actor);

        Assert.Equal(new[] { "lock", "read-queue" }, calls);
        Assert.Same(winner, offered);

        Assert.Equal(WaitlistStatus.Withdrawn, alreadyBooked.Status);
        Assert.Equal(WaitlistOfferer.AlreadyBookedReason, alreadyBooked.ClosedReason);
        Assert.Equal(WaitlistStatus.Offered, alreadyOffered.Status);   // untouched
        Assert.Equal(WaitlistStatus.Waiting, busyThen.Status);         // keeps place
        Assert.Equal(WaitlistStatus.Offered, winner.Status);
        Assert.Equal(slot.SlotId, winner.OfferedSlotId);
        Assert.Equal(SlotStatus.Offered, slot.Status);
        Assert.Equal(WaitlistStatus.Waiting, neverReached.Status);

        // The non-Waiting entry is skipped before any lookup; the walk stops at the winner.
        _waitlist.Verify(w => w.HasBookedWithDoctorOnAsync(alreadyOffered.PatientId, It.IsAny<Guid>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()), Times.Never);
        _waitlist.Verify(w => w.HasBookedWithDoctorOnAsync(neverReached.PatientId, It.IsAny<Guid>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()), Times.Never);

        // The offerer stages changes only; the caller owns the single save.
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    [Trait(TestCategories.Technique, TestCategories.Mocking)]
    public async Task An_empty_queue_leaves_the_slot_Available()
    {
        GivenQueue();
        var slot = SlotBuilder.ASlot().Build();

        var entry = await CreateOfferer().OfferAsync(slot, TestClock.Now, Actor);

        Assert.Null(entry);
        Assert.Equal(SlotStatus.Available, slot.Status);
    }

    [Theory]
    [InlineData(60, 180, 60)]  // normal: one hour to accept
    [InlineData(60, 30, 30)]   // slot starts first: the offer cannot outlive the slot
    [Trait(TestCategories.Technique, TestCategories.Parameterized)]
    public async Task The_offer_expires_at_the_earlier_of_the_window_or_the_slot_start(
        int offerWindowMinutes, int minutesUntilStart, int expectedExpiryMinutes)
    {
        GivenQueue(Waiting(1));
        var slot = SlotBuilder.ASlot().StartingIn(TimeSpan.FromMinutes(minutesUntilStart)).Build();

        var entry = await CreateOfferer(offerWindowMinutes, minimumLeadMinutes: 0).OfferAsync(slot, TestClock.Now, Actor);

        Assert.NotNull(entry);
        Assert.Equal(TestClock.Now, entry!.OfferedAtUtc);
        Assert.Equal(TestClock.Now.AddMinutes(expectedExpiryMinutes), entry.OfferExpiresAtUtc);
    }

    // ── Passing an offer on (declined / expired) ─────────────────────────────

    [Fact]
    [Trait(TestCategories.Technique, TestCategories.Mocking)]
    public async Task A_declined_offer_frees_the_slot_saves_and_moves_to_the_next_patient()
    {
        var slot = SlotBuilder.ASlot().WithStatus(SlotStatus.Offered).StartingIn(TimeSpan.FromHours(5)).Build();
        var declining = Waiting(1, WaitlistStatus.Offered);
        declining.OfferedSlotId = slot.SlotId;
        var next = Waiting(2);
        GivenQueue(next);

        var reOffered = await CreateOfferer().PassOnAsync(
            declining, slot, WaitlistStatus.Declined, "Cannot make it", TestClock.Now, Actor);

        Assert.Equal(WaitlistStatus.Declined, declining.Status);
        Assert.Equal("Cannot make it", declining.ClosedReason);
        Assert.Equal(TestClock.Now, declining.ClosedAtUtc);
        Assert.Same(next, reOffered);
        Assert.Equal(SlotStatus.Offered, slot.Status);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    [Trait(TestCategories.Technique, TestCategories.Mocking)]
    public async Task Passing_on_an_offer_that_no_longer_holds_the_slot_closes_it_and_offers_nothing()
    {
        var entry = Waiting(1, WaitlistStatus.Offered);
        entry.OfferedSlotId = Guid.NewGuid(); // points at some other slot
        var slot = SlotBuilder.ASlot().WithStatus(SlotStatus.Offered).Build();

        var reOffered = await CreateOfferer().PassOnAsync(
            entry, slot, WaitlistStatus.Expired, null, TestClock.Now, Actor);

        Assert.Null(reOffered);
        Assert.Equal(WaitlistStatus.Expired, entry.Status);
        Assert.Equal(SlotStatus.Offered, slot.Status); // not ours to release
        _waitlist.Verify(w => w.GetWaitingTrackedAsync(It.IsAny<Guid>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()), Times.Never);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
