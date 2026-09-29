using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MediCore.Appointment.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DoctorLeaveApprovalWorkflow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_doctor_leaves_doctor_dates",
                schema: "medicore_appointment",
                table: "doctor_leaves");

            migrationBuilder.AddColumn<string>(
                name: "ReviewNotes",
                schema: "medicore_appointment",
                table: "doctor_leaves",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ReviewedAtUtc",
                schema: "medicore_appointment",
                table: "doctor_leaves",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReviewedBy",
                schema: "medicore_appointment",
                table: "doctor_leaves",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Status",
                schema: "medicore_appointment",
                table: "doctor_leaves",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Pending");

            migrationBuilder.CreateIndex(
                name: "ix_doctor_leaves_doctor_status_dates",
                schema: "medicore_appointment",
                table: "doctor_leaves",
                columns: new[] { "DoctorId", "Status", "StartDate", "EndDate" });

            migrationBuilder.CreateIndex(
                name: "ix_doctor_leaves_status_start",
                schema: "medicore_appointment",
                table: "doctor_leaves",
                columns: new[] { "Status", "StartDate" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_doctor_leaves_doctor_status_dates",
                schema: "medicore_appointment",
                table: "doctor_leaves");

            migrationBuilder.DropIndex(
                name: "ix_doctor_leaves_status_start",
                schema: "medicore_appointment",
                table: "doctor_leaves");

            migrationBuilder.DropColumn(
                name: "ReviewNotes",
                schema: "medicore_appointment",
                table: "doctor_leaves");

            migrationBuilder.DropColumn(
                name: "ReviewedAtUtc",
                schema: "medicore_appointment",
                table: "doctor_leaves");

            migrationBuilder.DropColumn(
                name: "ReviewedBy",
                schema: "medicore_appointment",
                table: "doctor_leaves");

            migrationBuilder.DropColumn(
                name: "Status",
                schema: "medicore_appointment",
                table: "doctor_leaves");

            migrationBuilder.CreateIndex(
                name: "ix_doctor_leaves_doctor_dates",
                schema: "medicore_appointment",
                table: "doctor_leaves",
                columns: new[] { "DoctorId", "StartDate", "EndDate" });
        }
    }
}
