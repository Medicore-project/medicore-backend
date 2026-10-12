using MediCore.Billing.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

namespace MediCore.Billing.Tests.Unit;

public sealed class BillingMigrationSqlTests
{
    [Fact]
    public void Notification_and_revenue_migrations_generate_sql_without_a_database()
    {
        using var context = CreateContext();

        var sql = context.GetService<IMigrator>().GenerateScript(
            "20261006173810_AddServiceTariffConstraints",
            "20261008110000_AddRevenueReporting");

        Assert.Contains("CREATE TABLE medicore_billing.notification_templates", sql);
        Assert.Contains("INSERT INTO medicore_billing.notification_templates", sql);
        Assert.Contains("20261008100000_AddNotifications", sql);
        Assert.Contains("ALTER TABLE medicore_billing.invoices ADD \"DepartmentId\" uuid", sql);
        Assert.Contains("20261008110000_AddRevenueReporting", sql);
    }

    [Fact]
    public void Retiring_follow_ups_deactivates_the_tariff_without_deleting_it()
    {
        using var context = CreateContext();

        var sql = context.GetService<IMigrator>().GenerateScript(
            "20261008110000_AddRevenueReporting",
            "20261012100000_RetireFollowUpTariff");

        Assert.Contains("UPDATE medicore_billing.service_tariffs", sql);
        Assert.Contains("SET \"IsActive\" = FALSE", sql);
        Assert.Contains("WHERE \"ServiceCode\" = 'FOLLOW-UP'", sql);
        Assert.DoesNotContain("DELETE FROM medicore_billing.service_tariffs", sql);
        Assert.Contains("20261012100000_RetireFollowUpTariff", sql);
    }

    [Fact]
    public void The_partial_payment_receipt_template_is_added_without_failing_on_a_code_already_taken()
    {
        using var context = CreateContext();

        var sql = context.GetService<IMigrator>().GenerateScript(
            "20261012100000_RetireFollowUpTariff",
            "20261012110000_AddPartialPaymentReceiptTemplate");

        Assert.Contains("INSERT INTO medicore_billing.notification_templates", sql);
        Assert.Contains("'PARTIAL_PAYMENT_RECEIPT'", sql);
        Assert.Contains("{{balanceDue}}", sql);
        Assert.Contains("ON CONFLICT (\"Code\") DO NOTHING", sql);
        Assert.Contains("20261012110000_AddPartialPaymentReceiptTemplate", sql);
    }

    [Fact]
    public void The_seeded_partial_receipt_template_is_the_one_the_sender_looks_for()
    {
        // The sender finds its template by code. If the seed and the constant ever drift apart,
        // every part-payment receipt fails on "template not found" and is dead-lettered.
        using var context = CreateContext();

        // Seed rows live only on the design-time model; the runtime one drops them.
        var seeded = context.GetService<IDesignTimeModel>().Model
            .FindEntityType(typeof(MediCore.Billing.Application.Entities.NotificationTemplate))!
            .GetSeedData()
            .Select(row => (string)row["Code"]!)
            .ToArray();

        Assert.Contains(MediCore.Billing.Application.Services.NotificationService.PartialReceiptTemplate, seeded);
        Assert.Contains(MediCore.Billing.Application.Services.NotificationService.ReceiptTemplate, seeded);
    }

    [Fact]
    public void The_model_has_no_changes_the_migrations_do_not_already_cover()
    {
        // Billing migrates on startup and refuses to start if the model has drifted from the
        // snapshot, so a seed or mapping change with no migration behind it would take the
        // service down. These migrations are written by hand, which is exactly when that happens.
        using var context = CreateContext();

        Assert.False(context.Database.HasPendingModelChanges());
    }

    private static BillingDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<BillingDbContext>()
            .UseNpgsql("Host=localhost;Database=medicore;Username=billing_svc;Password=unused")
            .Options);
}
