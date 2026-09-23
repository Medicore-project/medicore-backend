using MediCore.Appointment.Application.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using AppointmentEntity = MediCore.Appointment.Application.Entities.Appointment;

namespace MediCore.Appointment.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core fluent configuration for <see cref="AppointmentEntity"/>.
/// </summary>
public sealed class AppointmentConfiguration : IEntityTypeConfiguration<AppointmentEntity>
{
    /// <summary>
    /// Partial-index predicate backing SCRUM-34 AC1 — one active appointment per slot.
    /// <para>
    /// This index is what actually prevents double-booking. <c>ux_slots_doctor_start</c> cannot:
    /// booking <em>mutates</em> a slot rather than inserting one, so two concurrent bookings both
    /// read <c>Available</c> and both write <c>Booked</c> to the same row without conflict. Only
    /// the appointment insert collides, and exactly one survives.
    /// </para>
    /// <para>
    /// Soft-deleted rows are excluded per the house rule, so an administratively removed
    /// appointment never blocks its slot forever. Cancelled rows are excluded because cancelling
    /// releases the slot for someone else while the row stays as history — the same reasoning
    /// <c>ux_slots_doctor_start</c> gives for excluding Flagged.
    /// </para>
    /// </summary>
    internal const string ActiveBookingFilter =
        "\"IsDeleted\" = false AND \"Status\" <> 'Cancelled'";

    public void Configure(EntityTypeBuilder<AppointmentEntity> builder)
    {
        builder.ToTable("appointments");
        builder.HasKey(a => a.Id);

        // ── Business key ─────────────────────────────────────────────────────
        builder.Property(a => a.AppointmentId).IsRequired();

        // ── Foreign references held as plain columns ─────────────────────────
        // SlotId: the slot row may be hard-deleted by schedule revision, so no FK.
        // PatientId: that row lives in the Patient service's schema, so no FK is possible.
        // DoctorId: DoctorCache is eventually consistent, so no FK — as on Slot.
        // No relationship is configured at all, which is why OnDelete(Restrict) does not appear
        // here as it does elsewhere in this assembly.
        builder.Property(a => a.SlotId).IsRequired();
        builder.Property(a => a.PatientId).IsRequired();
        builder.Property(a => a.DoctorId).IsRequired();

        // ── Time window (denormalised from the slot) ─────────────────────────
        builder.Property(a => a.StartUtc).IsRequired();
        builder.Property(a => a.EndUtc).IsRequired();
        builder.Property(a => a.SlotDate).HasColumnType("date").IsRequired();
        builder.Property(a => a.DurationMinutes).IsRequired();

        // ── Billing / lifecycle ──────────────────────────────────────────────
        builder.Property(a => a.ServiceCode)
            .HasMaxLength(50)
            .HasDefaultValue(ServiceCodes.GeneralConsultation)
            .IsRequired();
        builder.Property(a => a.Status)
            .HasMaxLength(20)
            .HasDefaultValue(AppointmentStatus.Booked)
            .IsRequired();

        // ── Audit columns ────────────────────────────────────────────────────
        builder.Property(a => a.IsDeleted).HasDefaultValue(false).IsRequired();
        builder.Property(a => a.CreatedBy).HasMaxLength(100).IsRequired();
        builder.Property(a => a.UpdatedBy).HasMaxLength(100);

        // ── Indexes ──────────────────────────────────────────────────────────

        // Unique business key — one row per AppointmentId.
        builder.HasIndex(a => a.AppointmentId)
            .IsUnique()
            .HasDatabaseName("ux_appointments_appointment_id");

        // SCRUM-34 AC1 — see ActiveBookingFilter.
        builder.HasIndex(a => a.SlotId)
            .IsUnique()
            .HasFilter(ActiveBookingFilter)
            .HasDatabaseName("ux_appointments_slot");

        // SCRUM-34 AC3 — "does this patient already have something around then?".
        builder.HasIndex(a => new { a.PatientId, a.StartUtc })
            .HasDatabaseName("ix_appointments_patient_start");

        // ── Global query filter (soft-delete) ────────────────────────────────
        builder.HasQueryFilter(a => !a.IsDeleted);
    }
}
