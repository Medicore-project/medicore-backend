using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace MediCore.Billing.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialBilling : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "medicore_billing");

            migrationBuilder.CreateTable(
                name: "invoices",
                schema: "medicore_billing",
                columns: table => new
                {
                    InvoiceId = table.Column<Guid>(type: "uuid", nullable: false),
                    InvoiceNumber = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    AppointmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    PatientId = table.Column<Guid>(type: "uuid", nullable: false),
                    ServiceCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    Subtotal = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Total = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    RequiresManualPricing = table.Column<bool>(type: "boolean", nullable: false),
                    PricingIssue = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    IssuedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    FinalizedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_invoices", x => x.InvoiceId);
                });

            migrationBuilder.CreateTable(
                name: "processed_messages",
                schema: "medicore_billing",
                columns: table => new
                {
                    MessageId = table.Column<Guid>(type: "uuid", nullable: false),
                    EventType = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ConsumerGroup = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    SourceTopic = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Outcome = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ProcessedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_processed_messages", x => x.MessageId);
                });

            migrationBuilder.CreateTable(
                name: "service_tariffs",
                schema: "medicore_billing",
                columns: table => new
                {
                    TariffId = table.Column<Guid>(type: "uuid", nullable: false),
                    ServiceCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Description = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    UnitPrice = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    EffectiveFromUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EffectiveToUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_service_tariffs", x => x.TariffId);
                });

            migrationBuilder.CreateTable(
                name: "invoice_lines",
                schema: "medicore_billing",
                columns: table => new
                {
                    InvoiceLineId = table.Column<Guid>(type: "uuid", nullable: false),
                    InvoiceId = table.Column<Guid>(type: "uuid", nullable: false),
                    TariffId = table.Column<Guid>(type: "uuid", nullable: true),
                    ServiceCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Description = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Quantity = table.Column<int>(type: "integer", nullable: false),
                    UnitPrice = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    LineTotal = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_invoice_lines", x => x.InvoiceLineId);
                    table.ForeignKey(
                        name: "FK_invoice_lines_invoices_InvoiceId",
                        column: x => x.InvoiceId,
                        principalSchema: "medicore_billing",
                        principalTable: "invoices",
                        principalColumn: "InvoiceId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                schema: "medicore_billing",
                table: "service_tariffs",
                columns: new[] { "TariffId", "Currency", "Description", "EffectiveFromUtc", "EffectiveToUtc", "IsActive", "ServiceCode", "UnitPrice" },
                values: new object[,]
                {
                    { new Guid("8b4dcd4c-3cee-4fe6-a3d2-36c90cb0dd01"), "LKR", "General consultation", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, true, "GEN-CONSULT", 2500m },
                    { new Guid("8b4dcd4c-3cee-4fe6-a3d2-36c90cb0dd02"), "LKR", "Specialist consultation", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, true, "SPEC-CONSULT", 5000m },
                    { new Guid("8b4dcd4c-3cee-4fe6-a3d2-36c90cb0dd03"), "LKR", "Follow-up consultation", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, true, "FOLLOW-UP", 1500m }
                });

            migrationBuilder.CreateIndex(
                name: "IX_invoice_lines_InvoiceId",
                schema: "medicore_billing",
                table: "invoice_lines",
                column: "InvoiceId");

            migrationBuilder.CreateIndex(
                name: "ix_invoices_patient_issued_at",
                schema: "medicore_billing",
                table: "invoices",
                columns: new[] { "PatientId", "IssuedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "ux_invoices_appointment_id",
                schema: "medicore_billing",
                table: "invoices",
                column: "AppointmentId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_invoices_invoice_number",
                schema: "medicore_billing",
                table: "invoices",
                column: "InvoiceNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_processed_messages_consumer_processed_at",
                schema: "medicore_billing",
                table: "processed_messages",
                columns: new[] { "ConsumerGroup", "ProcessedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "ux_service_tariffs_code_effective_from",
                schema: "medicore_billing",
                table: "service_tariffs",
                columns: new[] { "ServiceCode", "EffectiveFromUtc" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "invoice_lines",
                schema: "medicore_billing");

            migrationBuilder.DropTable(
                name: "processed_messages",
                schema: "medicore_billing");

            migrationBuilder.DropTable(
                name: "service_tariffs",
                schema: "medicore_billing");

            migrationBuilder.DropTable(
                name: "invoices",
                schema: "medicore_billing");
        }
    }
}
