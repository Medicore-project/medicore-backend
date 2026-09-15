using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MediCore.Patient.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAllergies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "allergies",
                schema: "medicore_patient",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AllergyId = table.Column<Guid>(type: "uuid", nullable: false),
                    PatientId = table.Column<Guid>(type: "uuid", nullable: false),
                    Allergen = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Severity = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Reaction = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "Active"),
                    RecordedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    RecordedByClinicianId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    RecordedByClinicianEmail = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    RecordedByClinicianRole = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    UpdatedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_allergies", x => x.Id);
                    table.ForeignKey(
                        name: "FK_allergies_patients_PatientId",
                        column: x => x.PatientId,
                        principalSchema: "medicore_patient",
                        principalTable: "patients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ux_allergies_allergy_id",
                schema: "medicore_patient",
                table: "allergies",
                column: "AllergyId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_allergies_patient_status",
                schema: "medicore_patient",
                table: "allergies",
                columns: new[] { "PatientId", "Status" });

            migrationBuilder.CreateIndex(
                name: "ix_allergies_patient_recorded_at",
                schema: "medicore_patient",
                table: "allergies",
                columns: new[] { "PatientId", "RecordedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "allergies",
                schema: "medicore_patient");
        }
    }
}
