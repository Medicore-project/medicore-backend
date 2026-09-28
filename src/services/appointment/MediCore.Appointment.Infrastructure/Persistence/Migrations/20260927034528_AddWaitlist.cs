using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MediCore.Appointment.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWaitlist : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "waitlist_entries",
                schema: "medicore_appointment",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WaitlistEntryId = table.Column<Guid>(type: "uuid", nullable: false),
                    DoctorId = table.Column<Guid>(type: "uuid", nullable: false),
                    SlotDate = table.Column<DateOnly>(type: "date", nullable: false),
                    PatientId = table.Column<Guid>(type: "uuid", nullable: false),
                    PatientNumber = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    PatientName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    ServiceCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, defaultValue: "GEN-CONSULT"),
                    Position = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "Waiting"),
                    JoinedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    OfferedSlotId = table.Column<Guid>(type: "uuid", nullable: true),
                    OfferedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    OfferExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    AppointmentId = table.Column<Guid>(type: "uuid", nullable: true),
                    ClosedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ClosedReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    UpdatedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_waitlist_entries", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_waitlist_entries_offer_expiry",
                schema: "medicore_appointment",
                table: "waitlist_entries",
                columns: new[] { "Status", "OfferExpiresAtUtc" });

            migrationBuilder.CreateIndex(
                name: "ix_waitlist_entries_queue",
                schema: "medicore_appointment",
                table: "waitlist_entries",
                columns: new[] { "DoctorId", "SlotDate", "Status", "Position" });

            migrationBuilder.CreateIndex(
                name: "ux_waitlist_entries_entry_id",
                schema: "medicore_appointment",
                table: "waitlist_entries",
                column: "WaitlistEntryId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_waitlist_entries_offered_slot",
                schema: "medicore_appointment",
                table: "waitlist_entries",
                column: "OfferedSlotId",
                unique: true,
                filter: "\"IsDeleted\" = false AND \"Status\" = 'Offered'");

            migrationBuilder.CreateIndex(
                name: "ux_waitlist_entries_patient_active",
                schema: "medicore_appointment",
                table: "waitlist_entries",
                columns: new[] { "PatientId", "DoctorId", "SlotDate" },
                unique: true,
                filter: "\"IsDeleted\" = false AND \"Status\" IN ('Waiting', 'Offered')");

            migrationBuilder.CreateIndex(
                name: "ux_waitlist_entries_queue_position",
                schema: "medicore_appointment",
                table: "waitlist_entries",
                columns: new[] { "DoctorId", "SlotDate", "Position" },
                unique: true,
                filter: "\"IsDeleted\" = false");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "waitlist_entries",
                schema: "medicore_appointment");
        }
    }
}
