using DistributionSystem.Application.Services.Implementations;
using DistributionSystem.Infrastructure.Data.Repositories.Implementations;
using FluentAssertions;

namespace DistributionSystem.UnitTests;

public class DashboardTrashReportingTests
{
    [Fact]
    public async Task Admin_global_delete_removes_order_from_operational_counts_but_preserves_financial_history()
    {
        using var f = new OrderTrashFixture();
        var report = new ReportService(new UnitOfWork(f.Db));
        var before = await report.GetDashboardAsync();

        before.TotalOrders.Should().Be(1);
        before.PendingOrders.Should().Be(1);
        before.TotalSalesMonth.Should().Be(100m);

        await f.Service.AdminSoftDeleteAsync(f.OrderId, "Admin");
        var deleted = await report.GetDashboardAsync();

        deleted.TotalOrders.Should().Be(0);
        deleted.PendingOrders.Should().Be(0);
        deleted.TotalSalesMonth.Should().Be(before.TotalSalesMonth);

        await f.Service.AdminPurgeTrashAsync(
            [new() { Id = f.OrderId, Kind = "Order" }]);
        var purged = await report.GetDashboardAsync();

        purged.TotalOrders.Should().Be(0);
        purged.PendingOrders.Should().Be(0);
        purged.TotalSalesMonth.Should().Be(before.TotalSalesMonth);
        f.Db.Orders.Should().ContainSingle(x => x.Id == f.OrderId);
    }

    [Fact]
    public async Task Rep_personal_delete_does_not_change_Admin_operational_or_financial_reports()
    {
        using var f = new OrderTrashFixture();
        var report = new ReportService(new UnitOfWork(f.Db));
        var before = await report.GetDashboardAsync();

        await f.Service.RepSoftDeleteAsync(f.OrderId, f.RepUserId);
        var after = await report.GetDashboardAsync();

        after.TotalOrders.Should().Be(before.TotalOrders);
        after.PendingOrders.Should().Be(before.PendingOrders);
        after.TotalSalesMonth.Should().Be(before.TotalSalesMonth);
    }

    [Fact]
    public async Task Sales_report_preserves_deleted_order_as_financial_history()
    {
        using var f = new OrderTrashFixture();
        var report = new ReportService(new UnitOfWork(f.Db));
        var from = DateTime.UtcNow.AddDays(-1);
        var to = DateTime.UtcNow.AddDays(1);

        await f.Service.AdminSoftDeleteAsync(f.OrderId, "Admin");

        var historic = await report.GetSalesReportAsync(new()
        {
            FromDate = from, ToDate = to
        });

        historic.TotalSales.Should().Be(100m);
        historic.TotalOrders.Should().Be(1);
    }
}
