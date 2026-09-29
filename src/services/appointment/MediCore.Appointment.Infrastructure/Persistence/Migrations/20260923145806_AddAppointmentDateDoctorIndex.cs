using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MediCore.Appointment.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAppointmentDateDoctorIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_appointments_date_doctor",
                schema: "medicore_appointment",
                table: "appointments",
                columns: new[] { "SlotDate", "DoctorId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_appointments_date_doctor",
                schema: "medicore_appointment",
                table: "appointments");
        }
    }
}
