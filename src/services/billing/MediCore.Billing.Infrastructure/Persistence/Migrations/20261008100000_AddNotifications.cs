using MediCore.Billing.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MediCore.Billing.Infrastructure.Persistence.Migrations;

[DbContext(typeof(BillingDbContext))]
[Migration("20261008100000_AddNotifications")]
public sealed class AddNotifications : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "notification_templates",
            schema: "medicore_billing",
            columns: table => new
            {
                NotificationTemplateId = table.Column<Guid>(type: "uuid", nullable: false),
                Code = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                Name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                SubjectTemplate = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                BodyTemplate = table.Column<string>(type: "character varying(10000)", maxLength: 10000, nullable: false),
                IsActive = table.Column<bool>(type: "boolean", nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table => table.PrimaryKey("PK_notification_templates", x => x.NotificationTemplateId));

        migrationBuilder.CreateTable(
            name: "patient_contacts",
            schema: "medicore_billing",
            columns: table => new
            {
                PatientId = table.Column<Guid>(type: "uuid", nullable: false),
                FullName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                Email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_patient_contacts", x => x.PatientId));

        migrationBuilder.CreateTable(
            name: "notification_logs",
            schema: "medicore_billing",
            columns: table => new
            {
                NotificationLogId = table.Column<Guid>(type: "uuid", nullable: false),
                NotificationTemplateId = table.Column<Guid>(type: "uuid", nullable: false),
                SourceMessageId = table.Column<Guid>(type: "uuid", nullable: false),
                EventType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                TemplateCode = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                Recipient = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                Subject = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                Body = table.Column<string>(type: "character varying(10000)", maxLength: 10000, nullable: false),
                Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                AttemptCount = table.Column<int>(type: "integer", nullable: false),
                CorrelationId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                Error = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                LastAttemptAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                SentAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_notification_logs", x => x.NotificationLogId);
                table.ForeignKey(
                    name: "FK_notification_logs_notification_templates_NotificationTemplateId",
                    column: x => x.NotificationTemplateId,
                    principalSchema: "medicore_billing",
                    principalTable: "notification_templates",
                    principalColumn: "NotificationTemplateId",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.InsertData(
            schema: "medicore_billing",
            table: "notification_templates",
            columns: new[] { "NotificationTemplateId", "BodyTemplate", "Code", "CreatedAtUtc", "IsActive", "Name", "SubjectTemplate", "UpdatedAtUtc" },
            columnTypes: new[] { "uuid", "character varying(10000)", "character varying(80)", "timestamp with time zone", "boolean", "character varying(150)", "character varying(300)", "timestamp with time zone" },
            values: new object[,]
            {
                { Guid.Parse("9c5dcd4c-3cee-4fe6-a3d2-36c90cb0ee01"), "Hello {{patientName}},\n\nYour MediCore patient profile is ready. Your patient reference is {{patientId}}.", "PATIENT_WELCOME", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), true, "Patient welcome", "Welcome to MediCore, {{patientName}}", null },
                { Guid.Parse("9c5dcd4c-3cee-4fe6-a3d2-36c90cb0ee02"), "Hello {{patientName}},\n\nYour {{serviceCode}} appointment is confirmed for {{slotStart}}. Appointment reference: {{appointmentId}}.", "APPOINTMENT_CONFIRMATION", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), true, "Appointment confirmation", "MediCore appointment confirmed", null },
                { Guid.Parse("9c5dcd4c-3cee-4fe6-a3d2-36c90cb0ee03"), "Hello {{patientName}},\n\nWe received {{amount}} LKR by {{method}} for invoice {{invoiceId}}. Thank you.", "PAYMENT_RECEIPT", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), true, "Payment receipt", "MediCore payment receipt", null }
            });

        migrationBuilder.CreateIndex(
            name: "ux_notification_templates_code",
            schema: "medicore_billing",
            table: "notification_templates",
            column: "Code",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_notification_logs_NotificationTemplateId",
            schema: "medicore_billing",
            table: "notification_logs",
            column: "NotificationTemplateId");

        migrationBuilder.CreateIndex(
            name: "ux_notification_logs_source_template",
            schema: "medicore_billing",
            table: "notification_logs",
            columns: new[] { "SourceMessageId", "TemplateCode" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "ix_notification_logs_status_created",
            schema: "medicore_billing",
            table: "notification_logs",
            columns: new[] { "Status", "CreatedAtUtc" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "notification_logs", schema: "medicore_billing");
        migrationBuilder.DropTable(name: "patient_contacts", schema: "medicore_billing");
        migrationBuilder.DropTable(name: "notification_templates", schema: "medicore_billing");
    }
}
