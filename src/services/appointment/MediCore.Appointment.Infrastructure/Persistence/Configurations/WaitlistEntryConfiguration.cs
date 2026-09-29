using MediCore.Appointment.Application.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MediCore.Appointment.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core fluent configuration for <see cref="WaitlistEntry"/>.
/// </summary>
/// <remarks>
/// No relationships are configured: the doctor, the patient, the offered slot and the resulting
/// appointment are all held as plain columns, for the reasons given on each property. That is why
/// <c>OnDelete(Restrict)</c> does not appear here. Writers serialise on a per-queue advisory lock
/// rather than a row version, so <c>Slot</c> remains the only entity with a concurrency token.
/// </remarks>
public sealed class WaitlistEntryConfiguration : IEntityTypeConfiguration<WaitlistEntry>
{
    /// <summary>
    /// A position is never reused within a queue. Closed entries stay inside the index — positions
    /// only ever grow — and soft-deleted rows are excluded per the house rule.
    /// </summary>
    internal const string QueuePositionFilter = "\"IsDeleted\" = false";

    /// <summary>
    /// One active entry per patient per queue. Closed entries are outside it, so a patient whose
    /// offer lapsed can join again at the back.
    /// </summary>
    internal const string PatientActiveFilter =
        "\"IsDeleted\" = false AND \"Status\" IN ('Waiting', 'Offered')";

    /// <summary>
    /// A slot is held for one entry at a time. The offer that went before it keeps its
    /// <c>OfferedSlotId</c> as a record, so only the open offer is constrained.
    /// </summary>
    internal const string OpenOfferFilter =
        "\"IsDeleted\" = false AND \"Status\" = 'Offered'";

    public void Configure(EntityTypeBuilder<WaitlistEntry> builder)
    {
        builder.ToTable("waitlist_entries");
        builder.HasKey(e => e.Id);

        // ── Business key ─────────────────────────────────────────────────────
        builder.Property(e => e.WaitlistEntryId).IsRequired();

        // ── The queue ────────────────────────────────────────────────────────
        builder.Property(e => e.DoctorId).IsRequired();
        builder.Property(e => e.SlotDate).HasColumnType("date").IsRequired();
        builder.Property(e => e.Position).IsRequired();

        // ── The patient (snapshot sized as on appointments) ──────────────────
        builder.Property(e => e.PatientId).IsRequired();
        builder.Property(e => e.PatientNumber).HasMaxLength(20);
        builder.Property(e => e.PatientName).HasMaxLength(256);
        builder.Property(e => e.ServiceCode)
            .HasMaxLength(50)
            .HasDefaultValue(ServiceCodes.GeneralConsultation)
            .IsRequired();

        // ── Lifecycle ────────────────────────────────────────────────────────
        builder.Property(e => e.Status)
            .HasMaxLength(20)
            .HasDefaultValue(WaitlistStatus.Waiting)
            .IsRequired();
        builder.Property(e => e.JoinedAtUtc).IsRequired();
        builder.Property(e => e.OfferedSlotId);
        builder.Property(e => e.OfferedAtUtc);
        builder.Property(e => e.OfferExpiresAtUtc);
        builder.Property(e => e.AppointmentId);
        builder.Property(e => e.ClosedAtUtc);
        builder.Property(e => e.ClosedReason).HasMaxLength(500);

        // ── Audit columns ────────────────────────────────────────────────────
        builder.Property(e => e.IsDeleted).HasDefaultValue(false).IsRequired();
        builder.Property(e => e.CreatedBy).HasMaxLength(100).IsRequired();
        builder.Property(e => e.UpdatedBy).HasMaxLength(100);

        // ── Indexes ──────────────────────────────────────────────────────────

        // Unique business key — one row per WaitlistEntryId.
        builder.HasIndex(e => e.WaitlistEntryId)
            .IsUnique()
            .HasDatabaseName("ux_waitlist_entries_entry_id");

        // AC1 — see QueuePositionFilter. The backstop behind the queue lock: two joins that somehow
        // computed the same next position cannot both commit.
        builder.HasIndex(e => new { e.DoctorId, e.SlotDate, e.Position })
            .IsUnique()
            .HasFilter(QueuePositionFilter)
            .HasDatabaseName("ux_waitlist_entries_queue_position");

        // See PatientActiveFilter.
        builder.HasIndex(e => new { e.PatientId, e.DoctorId, e.SlotDate })
            .IsUnique()
            .HasFilter(PatientActiveFilter)
            .HasDatabaseName("ux_waitlist_entries_patient_active");

        // See OpenOfferFilter.
        builder.HasIndex(e => e.OfferedSlotId)
            .IsUnique()
            .HasFilter(OpenOfferFilter)
            .HasDatabaseName("ux_waitlist_entries_offered_slot");

        // "Who is next in this queue" — the question every offer asks.
        builder.HasIndex(e => new { e.DoctorId, e.SlotDate, e.Status, e.Position })
            .HasDatabaseName("ix_waitlist_entries_queue");

        // The sweeper's "which offers have lapsed".
        builder.HasIndex(e => new { e.Status, e.OfferExpiresAtUtc })
            .HasDatabaseName("ix_waitlist_entries_offer_expiry");

        // ── Global query filter (soft-delete) ────────────────────────────────
        builder.HasQueryFilter(e => !e.IsDeleted);
    }
}
