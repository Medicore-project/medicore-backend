using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MediCore.Appointment.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAppointmentHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "appointment_history",
                schema: "medicore_appointment",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AppointmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Action = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    FromStatus = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    ToStatus = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    FromSlotId = table.Column<Guid>(type: "uuid", nullable: true),
                    ToSlotId = table.Column<Guid>(type: "uuid", nullable: true),
                    FromStartUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ToStartUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Actor = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_appointment_history", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_appointment_history_appointment_occurred",
                schema: "medicore_appointment",
                table: "appointment_history",
                columns: new[] { "AppointmentId", "OccurredAtUtc" });

            // SCRUM-36: every history starts with the booking that created the appointment, so
            // the appointments that already exist get that first entry here. Their later changes,
            // if any, predate this table and cannot be reconstructed; before this ticket nothing
            // but booking wrote to appointments, so there are none. gen_random_uuid() is built in
            // from Postgres 13; docker-compose runs 16.
            migrationBuilder.Sql(
                """
                INSERT INTO medicore_appointment.appointment_history
                    ("Id", "AppointmentId", "Action", "FromStatus", "ToStatus",
                     "FromSlotId", "ToSlotId", "FromStartUtc", "ToStartUtc",
                     "Reason", "Actor", "OccurredAtUtc")
                SELECT gen_random_uuid(), "AppointmentId", 'Booked', NULL, 'Booked',
                       NULL, "SlotId", NULL, "StartUtc",
                       NULL, "CreatedBy", "CreatedAt"
                FROM medicore_appointment.appointments;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "appointment_history",
                schema: "medicore_appointment");
        }
    }
}
