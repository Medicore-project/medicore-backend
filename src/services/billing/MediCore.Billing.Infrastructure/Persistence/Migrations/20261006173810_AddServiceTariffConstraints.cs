using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MediCore.Billing.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddServiceTariffConstraints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddCheckConstraint(
                name: "ck_service_tariffs_effective_dates",
                schema: "medicore_billing",
                table: "service_tariffs",
                sql: "\"EffectiveToUtc\" IS NULL OR \"EffectiveToUtc\" > \"EffectiveFromUtc\"");

            migrationBuilder.AddCheckConstraint(
                name: "ck_service_tariffs_unit_price",
                schema: "medicore_billing",
                table: "service_tariffs",
                sql: "\"UnitPrice\" > 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_service_tariffs_effective_dates",
                schema: "medicore_billing",
                table: "service_tariffs");

            migrationBuilder.DropCheckConstraint(
                name: "ck_service_tariffs_unit_price",
                schema: "medicore_billing",
                table: "service_tariffs");
        }
    }
}
