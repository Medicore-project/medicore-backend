using MediCore.Billing.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MediCore.Billing.Infrastructure.Persistence.Migrations;

/// <summary>
/// The template for the receipt a part payment now gets (<c>invoice.partially-paid</c>). Without
/// this row every such receipt would fail on "template not found" and end in the dead-letter topic.
/// </summary>
[DbContext(typeof(BillingDbContext))]
[Migration("20261012110000_AddPartialPaymentReceiptTemplate")]
public sealed class AddPartialPaymentReceiptTemplate : Migration
{
    private static readonly Guid PartialReceiptTemplateId = new("9c5dcd4c-3cee-4fe6-a3d2-36c90cb0ee04");

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Raw SQL for the ON CONFLICT: template codes are unique, and until recently an admin could
        // create templates by hand. A code already taken must not stop Billing from starting — the
        // sender looks a template up by its code, so whichever row holds it is the one used.
        migrationBuilder.Sql(
            """
            INSERT INTO medicore_billing.notification_templates
                ("NotificationTemplateId", "Code", "Name", "SubjectTemplate", "BodyTemplate", "IsActive", "CreatedAtUtc", "UpdatedAtUtc")
            VALUES
                ('9c5dcd4c-3cee-4fe6-a3d2-36c90cb0ee04',
                 'PARTIAL_PAYMENT_RECEIPT',
                 'Partial payment receipt',
                 'MediCore payment received',
                 E'Hello {{patientName}},\n\nWe received {{amount}} {{currency}} by {{method}} towards invoice {{invoiceNumber}}. Paid so far: {{totalPaid}} {{currency}}. Balance still due: {{balanceDue}} {{currency}}.',
                 TRUE,
                 TIMESTAMPTZ '2026-01-01 00:00:00+00',
                 NULL)
            ON CONFLICT ("Code") DO NOTHING;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DeleteData(
            schema: "medicore_billing",
            table: "notification_templates",
            keyColumn: "NotificationTemplateId",
            keyValue: PartialReceiptTemplateId);
    }
}
