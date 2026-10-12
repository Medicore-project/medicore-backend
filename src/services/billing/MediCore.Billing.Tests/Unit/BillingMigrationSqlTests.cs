using MediCore.Billing.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace MediCore.Billing.Tests.Unit;

public sealed class BillingMigrationSqlTests
{
    [Fact]
    public void Notification_and_revenue_migrations_generate_sql_without_a_database()
    {
        var options = new DbContextOptionsBuilder<BillingDbContext>()
            .UseNpgsql("Host=localhost;Database=medicore;Username=billing_svc;Password=unused")
            .Options;
        using var context = new BillingDbContext(options);

        var sql = context.GetService<IMigrator>().GenerateScript(
            "20261006173810_AddServiceTariffConstraints",
            "20261008110000_AddRevenueReporting");

        Assert.Contains("CREATE TABLE medicore_billing.notification_templates", sql);
        Assert.Contains("INSERT INTO medicore_billing.notification_templates", sql);
        Assert.Contains("20261008100000_AddNotifications", sql);
        Assert.Contains("ALTER TABLE medicore_billing.invoices ADD \"DepartmentId\" uuid", sql);
        Assert.Contains("20261008110000_AddRevenueReporting", sql);
    }
}
