using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MediCore.Patient.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMedicalRecordsAndConditions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "medical_records",
                schema: "medicore_patient",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RecordId = table.Column<Guid>(type: "uuid", nullable: false),
                    PatientId = table.Column<Guid>(type: "uuid", nullable: false),
                    VisitReference = table.Column<Guid>(type: "uuid", nullable: false),
                    ClinicalNotes = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: false),
                    AuthorClinicianId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    AuthorClinicianEmail = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    AuthorClinicianRole = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    AuthoredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    PreviousVersionId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsCurrent = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    UpdatedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_medical_records", x => x.Id);
                    table.ForeignKey(
                        name: "FK_medical_records_medical_records_PreviousVersionId",
                        column: x => x.PreviousVersionId,
                        principalSchema: "medicore_patient",
                        principalTable: "medical_records",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_medical_records_patients_PatientId",
                        column: x => x.PatientId,
                        principalSchema: "medicore_patient",
                        principalTable: "patients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "conditions",
                schema: "medicore_patient",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MedicalRecordId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    ClinicalStatus = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    UpdatedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_conditions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_conditions_medical_records_MedicalRecordId",
                        column: x => x.MedicalRecordId,
                        principalSchema: "medicore_patient",
                        principalTable: "medical_records",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_conditions_medical_record_id",
                schema: "medicore_patient",
                table: "conditions",
                column: "MedicalRecordId");

            migrationBuilder.CreateIndex(
                name: "ix_medical_records_patient_current_authored",
                schema: "medicore_patient",
                table: "medical_records",
                columns: new[] { "PatientId", "IsCurrent", "AuthoredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_medical_records_PreviousVersionId",
                schema: "medicore_patient",
                table: "medical_records",
                column: "PreviousVersionId");

            migrationBuilder.CreateIndex(
                name: "ix_medical_records_visit_reference",
                schema: "medicore_patient",
                table: "medical_records",
                column: "VisitReference");

            migrationBuilder.CreateIndex(
                name: "ux_medical_records_current_record",
                schema: "medicore_patient",
                table: "medical_records",
                column: "RecordId",
                unique: true,
                filter: "\"IsCurrent\" = true AND \"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "ux_medical_records_record_version",
                schema: "medicore_patient",
                table: "medical_records",
                columns: new[] { "RecordId", "Version" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "conditions",
                schema: "medicore_patient");

            migrationBuilder.DropTable(
                name: "medical_records",
                schema: "medicore_patient");
        }
    }
}
