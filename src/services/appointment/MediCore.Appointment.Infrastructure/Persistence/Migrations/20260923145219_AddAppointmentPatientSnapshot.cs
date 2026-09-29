using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MediCore.Appointment.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAppointmentPatientSnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PatientName",
                schema: "medicore_appointment",
                table: "appointments",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PatientNumber",
                schema: "medicore_appointment",
                table: "appointments",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PatientName",
                schema: "medicore_appointment",
                table: "appointments");

            migrationBuilder.DropColumn(
                name: "PatientNumber",
                schema: "medicore_appointment",
                table: "appointments");
        }
    }
}
