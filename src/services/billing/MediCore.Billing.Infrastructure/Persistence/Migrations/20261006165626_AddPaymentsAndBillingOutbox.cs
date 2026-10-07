using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MediCore.Billing.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPaymentsAndBillingOutbox : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "AmountPaid",
                schema: "medicore_billing",
                table: "invoices",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<DateTime>(
                name: "PaidAtUtc",
                schema: "medicore_billing",
                table: "invoices",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Version",
                schema: "medicore_billing",
                table: "invoices",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "outbox_messages",
                schema: "medicore_billing",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MessageId = table.Column<Guid>(type: "uuid", nullable: false),
                    Topic = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    EventKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    EventType = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    EventVersion = table.Column<int>(type: "integer", nullable: false),
                    CorrelationId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Payload = table.Column<string>(type: "jsonb", nullable: false),
                    OccurredOnUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ProcessedOnUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RetryCount = table.Column<int>(type: "integer", nullable: false),
                    Error = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_outbox_messages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "payments",
                schema: "medicore_billing",
                columns: table => new
                {
                    PaymentId = table.Column<Guid>(type: "uuid", nullable: false),
                    InvoiceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Method = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    RecordedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RecordedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_payments", x => x.PaymentId);
                    table.CheckConstraint("ck_payments_amount_positive", "\"Amount\" > 0");
                    table.ForeignKey(
                        name: "FK_payments_invoices_InvoiceId",
                        column: x => x.InvoiceId,
                        principalSchema: "medicore_billing",
                        principalTable: "invoices",
                        principalColumn: "InvoiceId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_invoices_amount_paid",
                schema: "medicore_billing",
                table: "invoices",
                sql: "\"AmountPaid\" >= 0 AND \"AmountPaid\" <= \"Total\"");

            migrationBuilder.CreateIndex(
                name: "IX_outbox_messages_MessageId",
                schema: "medicore_billing",
                table: "outbox_messages",
                column: "MessageId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_outbox_messages_ProcessedOnUtc_OccurredOnUtc",
                schema: "medicore_billing",
                table: "outbox_messages",
                columns: new[] { "ProcessedOnUtc", "OccurredOnUtc" });

            migrationBuilder.CreateIndex(
                name: "ix_payments_invoice_recorded_at",
                schema: "medicore_billing",
                table: "payments",
                columns: new[] { "InvoiceId", "RecordedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "outbox_messages",
                schema: "medicore_billing");

            migrationBuilder.DropTable(
                name: "payments",
                schema: "medicore_billing");

            migrationBuilder.DropCheckConstraint(
                name: "ck_invoices_amount_paid",
                schema: "medicore_billing",
                table: "invoices");

            migrationBuilder.DropColumn(
                name: "AmountPaid",
                schema: "medicore_billing",
                table: "invoices");

            migrationBuilder.DropColumn(
                name: "PaidAtUtc",
                schema: "medicore_billing",
                table: "invoices");

            migrationBuilder.DropColumn(
                name: "Version",
                schema: "medicore_billing",
                table: "invoices");
        }
    }
}
