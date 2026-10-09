using MediCore.Billing.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace MediCore.Billing.Infrastructure.Persistence.Migrations;

[DbContext(typeof(BillingDbContext))]
[Migration("20261008110000_AddRevenueReporting")]
public sealed class AddRevenueReporting : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(
            name: "DepartmentId",
            schema: "medicore_billing",
            table: "invoices",
            type: "uuid",
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "ix_invoices_department_id",
            schema: "medicore_billing",
            table: "invoices",
            column: "DepartmentId");

        migrationBuilder.CreateIndex(
            name: "ix_payments_recorded_at",
            schema: "medicore_billing",
            table: "payments",
            column: "RecordedAtUtc");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex("ix_payments_recorded_at", "medicore_billing", "payments");
        migrationBuilder.DropIndex("ix_invoices_department_id", "medicore_billing", "invoices");
        migrationBuilder.DropColumn("DepartmentId", "medicore_billing", "invoices");
    }
}
