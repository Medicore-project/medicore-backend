using MediCore.Appointment.Application.Entities;
using MediCore.Appointment.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace MediCore.Appointment.Tests.Unit;

public sealed class WaitlistModelTests
{
    [Fact]
    public void Waitlist_entries_map_to_their_own_table_with_a_soft_delete_filter()
    {
        var entityType = EntityType();

        Assert.Equal("waitlist_entries", entityType.GetTableName());
        Assert.Equal(AppointmentDbContext.SchemaName, entityType.GetSchema());
        Assert.NotNull(entityType.GetQueryFilter());
    }

    [Fact]
    public void The_entry_id_is_the_unique_business_key()
    {
        var index = Index("ux_waitlist_entries_entry_id");

        Assert.True(index.IsUnique);
        Assert.Equal([nameof(WaitlistEntry.WaitlistEntryId)], Columns(index));
    }

    [Fact]
    public void A_position_is_unique_within_its_queue_and_closed_entries_keep_theirs()
    {
        // AC1. Closed entries stay inside the index, so a position is never handed out twice —
        // the "place in line" shown to a patient is derived, never stored.
        var index = Index("ux_waitlist_entries_queue_position");

        Assert.True(index.IsUnique);
        Assert.Equal(
            [nameof(WaitlistEntry.DoctorId), nameof(WaitlistEntry.SlotDate), nameof(WaitlistEntry.Position)],
            Columns(index));
        Assert.Equal("\"IsDeleted\" = false", index.GetFilter());
    }

    [Fact]
    public void A_patient_holds_one_active_entry_per_queue_and_can_rejoin_once_it_closes()
    {
        // AppointmentUnitOfWork matches this index by name, so the filter is asserted byte for byte.
        var index = Index("ux_waitlist_entries_patient_active");

        Assert.True(index.IsUnique);
        Assert.Equal(
            [nameof(WaitlistEntry.PatientId), nameof(WaitlistEntry.DoctorId), nameof(WaitlistEntry.SlotDate)],
            Columns(index));
        Assert.Equal("\"IsDeleted\" = false AND \"Status\" IN ('Waiting', 'Offered')", index.GetFilter());
    }

    [Fact]
    public void A_slot_is_held_for_one_open_offer_at_a_time()
    {
        var index = Index("ux_waitlist_entries_offered_slot");

        Assert.True(index.IsUnique);
        Assert.Equal([nameof(WaitlistEntry.OfferedSlotId)], Columns(index));
        Assert.Equal("\"IsDeleted\" = false AND \"Status\" = 'Offered'", index.GetFilter());
    }

    [Fact]
    public void Who_is_next_and_which_offers_have_lapsed_are_both_indexed()
    {
        var queue = Index("ix_waitlist_entries_queue");
        var expiry = Index("ix_waitlist_entries_offer_expiry");

        Assert.False(queue.IsUnique);
        Assert.Equal(
            [
                nameof(WaitlistEntry.DoctorId), nameof(WaitlistEntry.SlotDate),
                nameof(WaitlistEntry.Status), nameof(WaitlistEntry.Position)
            ],
            Columns(queue));
        Assert.False(expiry.IsUnique);
        Assert.Equal(
            [nameof(WaitlistEntry.Status), nameof(WaitlistEntry.OfferExpiresAtUtc)],
            Columns(expiry));
    }

    [Fact]
    public void Nothing_is_a_foreign_key()
    {
        // The offered slot may be hard-deleted by schedule revision; the patient lives in another
        // service; the doctor cache is eventually consistent.
        Assert.Empty(EntityType().GetForeignKeys());
    }

    [Fact]
    public void A_new_entry_is_waiting_and_generally_billed()
    {
        var entry = new WaitlistEntry();

        Assert.Equal(WaitlistStatus.Waiting, entry.Status);
        Assert.Equal(ServiceCodes.GeneralConsultation, entry.ServiceCode);
        Assert.NotEqual(Guid.Empty, entry.WaitlistEntryId);
    }

    [Fact]
    public void Columns_are_sized_to_what_they_hold()
    {
        var entityType = EntityType();

        Assert.Equal(20, entityType.FindProperty(nameof(WaitlistEntry.PatientNumber))!.GetMaxLength());
        Assert.Equal(256, entityType.FindProperty(nameof(WaitlistEntry.PatientName))!.GetMaxLength());
        Assert.Equal(50, entityType.FindProperty(nameof(WaitlistEntry.ServiceCode))!.GetMaxLength());
        Assert.Equal(20, entityType.FindProperty(nameof(WaitlistEntry.Status))!.GetMaxLength());
        Assert.Equal(500, entityType.FindProperty(nameof(WaitlistEntry.ClosedReason))!.GetMaxLength());
        Assert.Equal("date", entityType.FindProperty(nameof(WaitlistEntry.SlotDate))!.GetColumnType());
    }

    [Theory]
    [InlineData(WaitlistStatus.Waiting, true)]
    [InlineData(WaitlistStatus.Offered, true)]
    [InlineData(WaitlistStatus.Accepted, false)]
    [InlineData(WaitlistStatus.Declined, false)]
    [InlineData(WaitlistStatus.Expired, false)]
    [InlineData(WaitlistStatus.Withdrawn, false)]
    [InlineData(null, false)]
    public void Only_waiting_and_offered_entries_are_active(string? status, bool active)
    {
        // The same two statuses ux_waitlist_entries_patient_active covers.
        Assert.Equal(active, WaitlistStatus.IsActive(status));
    }

    private static IEntityType EntityType()
    {
        using var context = new AppointmentDbContext(
            new DbContextOptionsBuilder<AppointmentDbContext>()
                .UseNpgsql("Host=localhost;Database=unused;Username=unused;Password=unused")
                .Options);

        return context.Model.FindEntityType(typeof(WaitlistEntry))!;
    }

    private static IIndex Index(string name) =>
        Assert.Single(EntityType().GetIndexes(), index => index.GetDatabaseName() == name);

    private static IEnumerable<string> Columns(IIndex index) =>
        index.Properties.Select(property => property.Name);
}
