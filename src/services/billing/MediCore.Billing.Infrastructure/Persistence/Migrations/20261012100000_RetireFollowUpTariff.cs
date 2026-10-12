using MediCore.Billing.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MediCore.Billing.Infrastructure.Persistence.Migrations;

/// <summary>
/// Follow-up visits are no longer a bookable service, so no appointment can carry the
/// <c>FOLLOW-UP</c> code again. Its tariff is deactivated rather than deleted: invoices already
/// issued at that price keep the tariff row their lines point to.
/// </summary>
[DbContext(typeof(BillingDbContext))]
[Migration("20261012100000_RetireFollowUpTariff")]
public sealed class RetireFollowUpTariff : Migration
{
    private static readonly Guid FollowUpTariffId = new("8b4dcd4c-3cee-4fe6-a3d2-36c90cb0dd03");

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Every version of the code, not only the seeded row: an Admin may have scheduled later
        // prices for it, and deactivating a tariff means deactivating all of its versions.
        migrationBuilder.Sql(
            """
            UPDATE medicore_billing.service_tariffs
            SET "IsActive" = FALSE
            WHERE "ServiceCode" = 'FOLLOW-UP';
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Only the seeded row is restored, which is all the previous model states about it.
        migrationBuilder.UpdateData(
            schema: "medicore_billing",
            table: "service_tariffs",
            keyColumn: "TariffId",
            keyValue: FollowUpTariffId,
            column: "IsActive",
            value: true);
    }
}
