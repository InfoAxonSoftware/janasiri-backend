using DistributionSystem.Application.DTOs.TargetReports;
using DistributionSystem.Application.Exceptions;
using DistributionSystem.Application.Services.Implementations;
using DistributionSystem.Domain.Entities;
using DistributionSystem.Infrastructure.Data;
using DistributionSystem.Infrastructure.Data.Repositories.Implementations;
using DistributionSystem.Infrastructure.Data.Repositories.Interfaces;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace DistributionSystem.UnitTests;

public class TargetReportServiceFixture : IDisposable
{
    public ApplicationDbContext Db { get; }
    public IUnitOfWork Uow { get; }
    public TargetReportService Service { get; }

    public Guid RepAUserId = Guid.NewGuid();
    public Guid RepAProfileId = Guid.NewGuid();
    public Guid RepBUserId = Guid.NewGuid();
    public Guid RepBProfileId = Guid.NewGuid();
    public Guid AdminUserId = Guid.NewGuid();

    public TargetReportServiceFixture()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        Db = new ApplicationDbContext(options);
        Uow = new UnitOfWork(Db);
        Service = new TargetReportService(Uow);

        SeedAsync().GetAwaiter().GetResult();
    }

    private async Task SeedAsync()
    {
        Db.SalesRepProfiles.AddRange(
            new SalesRepProfile { Id = RepAProfileId, UserId = RepAUserId, FullName = "Rep A", EmployeeCode = "RA1" },
            new SalesRepProfile { Id = RepBProfileId, UserId = RepBUserId, FullName = "Rep B", EmployeeCode = "RB1" });

        await Db.SaveChangesAsync();
    }

    public async Task<SalesTarget> CreateTargetAsync(Guid repId, DateTime start, DateTime end, decimal amount)
    {
        var target = new SalesTarget
        {
            RepId = repId,
            TargetPeriod = "Monthly",
            StartDate = start,
            EndDate = end,
            TargetAmount = amount,
            Status = "Active",
        };
        Db.Set<SalesTarget>().Add(target);
        await Db.SaveChangesAsync();
        return target;
    }

    public static UploadTargetReportRequest BuildRequest(
        (int day, decimal salesWithTax)[] rows, int month = 6, int year = 2026, bool force = false) => new()
    {
        OriginalFileName = "Detailed sales Report.xlsx",
        Force = force,
        Entries = rows.Select((r, i) => new TargetReportEntryRequest
        {
            TxnDate = new DateTime(year, month, r.day, 0, 0, 0, DateTimeKind.Utc),
            RefNo = $"K{1000 + i}",
            CustomerName = $"Customer {i}",
            ItemDescription = "Nelna Chicken",
            Qty = 10 + i,
            Discount = 0,
            SalesWithTax = r.salesWithTax,
            SortOrder = i,
        }).ToList(),
    };

    public void Dispose() => Db.Dispose();
}

public class TargetReportUploadTests : IClassFixture<TargetReportServiceFixture>
{
    private readonly TargetReportServiceFixture _f;
    public TargetReportUploadTests(TargetReportServiceFixture f) => _f = f;

    [Fact]
    public async Task Upload_ComputesActualSales_AsSumOfSalesWithTax()
    {
        var target = await _f.CreateTargetAsync(_f.RepAProfileId, new DateTime(2026, 6, 1), new DateTime(2026, 6, 30), 2_000_000m);
        var request = TargetReportServiceFixture.BuildRequest([(1, 100_000m), (2, 150_000m), (3, 50_000m)]);

        var result = await _f.Service.UploadReportAsync(target.Id, request, _f.AdminUserId, "Admin User");

        result.ActualSales.Should().Be(300_000m);
        result.IsCurrent.Should().BeTrue();
    }

    [Fact]
    public async Task Upload_DerivesFromDateAndAsAtDate_AsMinAndMaxOfInvoiceDates()
    {
        var target = await _f.CreateTargetAsync(_f.RepAProfileId, new DateTime(2026, 6, 1), new DateTime(2026, 6, 30), 1_000_000m);
        var request = TargetReportServiceFixture.BuildRequest([(5, 10_000m), (1, 20_000m), (25, 30_000m)]);

        var result = await _f.Service.UploadReportAsync(target.Id, request, _f.AdminUserId, "Admin User");

        result.FromDate.Should().Be(new DateTime(2026, 6, 1));
        result.AsAtDate.Should().Be(new DateTime(2026, 6, 25));
    }

    [Fact]
    public async Task Upload_ComputesDistinctOrderAndCustomerCounts_IgnoringDuplicates()
    {
        var target = await _f.CreateTargetAsync(_f.RepAProfileId, new DateTime(2026, 6, 1), new DateTime(2026, 6, 30), 1_000_000m);
        var request = new UploadTargetReportRequest
        {
            OriginalFileName = "Detailed sales Report.xlsx",
            Entries =
            [
                new TargetReportEntryRequest { TxnDate = new DateTime(2026, 6, 1), RefNo = "K114467", CustomerName = "Scottish Planter Glendevon Bungalow - Ragala", SalesWithTax = 18_334.52m, SortOrder = 0 },
                // Same order (RefNo), same customer, second line item — must not double-count the order or customer.
                new TargetReportEntryRequest { TxnDate = new DateTime(2026, 6, 1), RefNo = "K114467", CustomerName = "Scottish Planter Glendevon Bungalow - Ragala", SalesWithTax = 22_106.01m, SortOrder = 1 },
                // Same customer, extra/collapsed whitespace and different case — must normalize to the same customer.
                new TargetReportEntryRequest { TxnDate = new DateTime(2026, 6, 3), RefNo = "K114606", CustomerName = "  Scottish Planter Glendevon   Bungalow - Ragala ", SalesWithTax = 38_129.73m, SortOrder = 2 },
                new TargetReportEntryRequest { TxnDate = new DateTime(2026, 6, 5), RefNo = "K114916", CustomerName = "Suwasewana Hospital Ltd - Kandy - (Vat Customer)", SalesWithTax = 84_474.41m, SortOrder = 3 },
            ],
        };

        var result = await _f.Service.UploadReportAsync(target.Id, request, _f.AdminUserId, "Admin User");

        result.DistinctOrderCount.Should().Be(3); // K114467, K114606, K114916
        result.DistinctCustomerCount.Should().Be(2); // Scottish Planter..., Suwasewana Hospital...
    }

    [Fact]
    public async Task Upload_PreservesNegativeDiscountValues()
    {
        var target = await _f.CreateTargetAsync(_f.RepAProfileId, new DateTime(2026, 6, 1), new DateTime(2026, 6, 30), 1_000_000m);
        var request = new UploadTargetReportRequest
        {
            Entries =
            [
                new TargetReportEntryRequest { TxnDate = new DateTime(2026, 6, 4), RefNo = "K114742", CustomerName = "RED CHILLIES", Discount = -2_217.99m, SalesWithTax = 80_266.00m, SortOrder = 0 },
            ],
        };

        await _f.Service.UploadReportAsync(target.Id, request, _f.AdminUserId, "Admin User");

        var report = await _f.Service.GetCurrentReportAsync(target.Id);
        report.Entries.Single().Discount.Should().Be(-2_217.99m);
    }

    [Fact]
    public async Task Upload_RejectsEmptyEntries()
    {
        var target = await _f.CreateTargetAsync(_f.RepAProfileId, new DateTime(2026, 6, 1), new DateTime(2026, 6, 30), 1_000_000m);
        var request = new UploadTargetReportRequest { Entries = [] };

        var act = async () => await _f.Service.UploadReportAsync(target.Id, request, _f.AdminUserId, "Admin User");

        await act.Should().ThrowAsync<BusinessException>();
    }

    [Fact]
    public async Task Upload_RejectsFutureInvoiceDates()
    {
        var target = await _f.CreateTargetAsync(_f.RepAProfileId, DateTime.UtcNow.Date, DateTime.UtcNow.Date.AddMonths(1), 1_000_000m);
        var request = new UploadTargetReportRequest
        {
            Entries =
            [
                new TargetReportEntryRequest { TxnDate = DateTime.UtcNow.Date.AddDays(5), SalesWithTax = 10_000m, SortOrder = 0 },
            ],
        };

        var act = async () => await _f.Service.UploadReportAsync(target.Id, request, _f.AdminUserId, "Admin User");

        (await act.Should().ThrowAsync<BusinessException>()).Which.ErrorCode.Should().Be("FUTURE_DATE");
    }

    [Fact]
    public async Task Upload_RejectsInvoiceRowsSpanningMultipleMonths()
    {
        var target = await _f.CreateTargetAsync(_f.RepAProfileId, new DateTime(2026, 6, 1), new DateTime(2026, 7, 31), 1_000_000m);
        var request = new UploadTargetReportRequest
        {
            Entries =
            [
                new TargetReportEntryRequest { TxnDate = new DateTime(2026, 6, 28), SalesWithTax = 10_000m, SortOrder = 0 },
                new TargetReportEntryRequest { TxnDate = new DateTime(2026, 7, 1), SalesWithTax = 10_000m, SortOrder = 1 },
            ],
        };

        var act = async () => await _f.Service.UploadReportAsync(target.Id, request, _f.AdminUserId, "Admin User");

        (await act.Should().ThrowAsync<BusinessException>()).Which.ErrorCode.Should().Be("MULTIPLE_MONTHS");
    }

    [Fact]
    public async Task Upload_RejectsReportPeriodOutsideTargetPeriod()
    {
        var target = await _f.CreateTargetAsync(_f.RepAProfileId, new DateTime(2026, 6, 1), new DateTime(2026, 6, 30), 1_000_000m);
        // May invoices — outside the June target period
        var request = TargetReportServiceFixture.BuildRequest([(10, 10_000m)], month: 5);

        var act = async () => await _f.Service.UploadReportAsync(target.Id, request, _f.AdminUserId, "Admin User");

        (await act.Should().ThrowAsync<BusinessException>()).Which.ErrorCode.Should().Be("PERIOD_MISMATCH");
    }

    [Fact]
    public async Task Upload_UpdatesTargetAchievedAmount_ToActualSales()
    {
        var target = await _f.CreateTargetAsync(_f.RepAProfileId, new DateTime(2026, 6, 1), new DateTime(2026, 6, 30), 500_000m);
        var request = TargetReportServiceFixture.BuildRequest([(1, 200_000m)]);

        await _f.Service.UploadReportAsync(target.Id, request, _f.AdminUserId, "Admin User");

        var updated = await _f.Db.Set<SalesTarget>().FindAsync(target.Id);
        updated!.AchievedAmount.Should().Be(200_000m);
    }
}

public class TargetReportCumulativeReplaceTests : IClassFixture<TargetReportServiceFixture>
{
    private readonly TargetReportServiceFixture _f;
    public TargetReportCumulativeReplaceTests(TargetReportServiceFixture f) => _f = f;

    [Fact]
    public async Task LaterUpload_ReplacesActualSales_DoesNotAddToPrevious()
    {
        var target = await _f.CreateTargetAsync(_f.RepAProfileId, new DateTime(2026, 6, 1), new DateTime(2026, 6, 30), 5_000_000m);

        var first = TargetReportServiceFixture.BuildRequest([(1, 800_000m), (5, 1_200_000m)]); // as at 06/05, total 2,000,000
        await _f.Service.UploadReportAsync(target.Id, first, _f.AdminUserId, "Admin User");

        var second = TargetReportServiceFixture.BuildRequest([(1, 800_000m), (5, 1_200_000m), (25, 2_100_000m)]); // as at 06/25, cumulative total
        var result = await _f.Service.UploadReportAsync(target.Id, second, _f.AdminUserId, "Admin User");

        result.ActualSales.Should().Be(4_100_000m); // NOT 2,000,000 + 4,100,000

        var updated = await _f.Db.Set<SalesTarget>().FindAsync(target.Id);
        updated!.AchievedAmount.Should().Be(4_100_000m);
    }

    [Fact]
    public async Task OnlyLatestUpload_IsMarkedCurrent_PreviousKeptAsHistory()
    {
        var target = await _f.CreateTargetAsync(_f.RepAProfileId, new DateTime(2026, 6, 1), new DateTime(2026, 6, 30), 5_000_000m);

        var first = TargetReportServiceFixture.BuildRequest([(1, 500_000m)]);
        var firstResult = await _f.Service.UploadReportAsync(target.Id, first, _f.AdminUserId, "Admin User");

        var second = TargetReportServiceFixture.BuildRequest([(1, 500_000m), (10, 700_000m)]);
        var secondResult = await _f.Service.UploadReportAsync(target.Id, second, _f.AdminUserId, "Admin User");

        var history = await _f.Service.GetHistoryAsync(target.Id);
        history.Should().HaveCount(2);
        history.Single(h => h.Id == secondResult.Id).IsCurrent.Should().BeTrue();
        history.Single(h => h.Id == firstResult.Id).IsCurrent.Should().BeFalse();

        var current = await _f.Service.GetCurrentReportAsync(target.Id);
        current.Id.Should().Be(secondResult.Id);
    }

    [Fact]
    public async Task OlderAsAtDate_WithoutForce_ThrowsConfirmationRequired()
    {
        var target = await _f.CreateTargetAsync(_f.RepAProfileId, new DateTime(2026, 6, 1), new DateTime(2026, 6, 30), 5_000_000m);

        var newer = TargetReportServiceFixture.BuildRequest([(20, 1_000_000m)]);
        await _f.Service.UploadReportAsync(target.Id, newer, _f.AdminUserId, "Admin User");

        var older = TargetReportServiceFixture.BuildRequest([(5, 400_000m)], force: false);
        var act = async () => await _f.Service.UploadReportAsync(target.Id, older, _f.AdminUserId, "Admin User");

        (await act.Should().ThrowAsync<BusinessException>()).Which.ErrorCode.Should().Be("OLDER_REPORT_CONFIRMATION_REQUIRED");
    }

    [Fact]
    public async Task OlderAsAtDate_WithForce_ReplacesCurrentReport()
    {
        var target = await _f.CreateTargetAsync(_f.RepAProfileId, new DateTime(2026, 6, 1), new DateTime(2026, 6, 30), 5_000_000m);

        var newer = TargetReportServiceFixture.BuildRequest([(20, 1_000_000m)]);
        await _f.Service.UploadReportAsync(target.Id, newer, _f.AdminUserId, "Admin User");

        var older = TargetReportServiceFixture.BuildRequest([(5, 400_000m)], force: true);
        var result = await _f.Service.UploadReportAsync(target.Id, older, _f.AdminUserId, "Admin User");

        result.ActualSales.Should().Be(400_000m);
        result.IsCurrent.Should().BeTrue();

        var current = await _f.Service.GetCurrentReportAsync(target.Id);
        current.Id.Should().Be(result.Id);
    }
}

public class TargetReportAccessTests : IClassFixture<TargetReportServiceFixture>
{
    private readonly TargetReportServiceFixture _f;
    public TargetReportAccessTests(TargetReportServiceFixture f) => _f = f;

    [Fact]
    public async Task Rep_CanAccess_OwnTargetReport()
    {
        var target = await _f.CreateTargetAsync(_f.RepAProfileId, new DateTime(2026, 6, 1), new DateTime(2026, 6, 30), 1_000_000m);
        var request = TargetReportServiceFixture.BuildRequest([(1, 300_000m)]);
        await _f.Service.UploadReportAsync(target.Id, request, _f.AdminUserId, "Admin User");

        var report = await _f.Service.GetRepCurrentReportAsync(target.Id, _f.RepAUserId);
        report.TargetId.Should().Be(target.Id);
    }

    [Fact]
    public async Task Rep_CannotAccess_AnotherRepsTargetReport()
    {
        var target = await _f.CreateTargetAsync(_f.RepAProfileId, new DateTime(2026, 6, 1), new DateTime(2026, 6, 30), 1_000_000m);
        var request = TargetReportServiceFixture.BuildRequest([(1, 300_000m)]);
        await _f.Service.UploadReportAsync(target.Id, request, _f.AdminUserId, "Admin User");

        var act = async () => await _f.Service.GetRepCurrentReportAsync(target.Id, _f.RepBUserId);

        await act.Should().ThrowAsync<NotFoundException>();
    }
}

public class TargetReportMultiTargetIsolationTests : IClassFixture<TargetReportServiceFixture>
{
    private readonly TargetReportServiceFixture _f;
    public TargetReportMultiTargetIsolationTests(TargetReportServiceFixture f) => _f = f;

    [Fact]
    public async Task SameRep_SameMonth_TargetReportsAndHistoryRemainIndependent()
    {
        var targetA = await _f.CreateTargetAsync(
            _f.RepAProfileId,
            new DateTime(2026, 6, 1),
            new DateTime(2026, 6, 30),
            1_000_000m);
        var targetB = await _f.CreateTargetAsync(
            _f.RepAProfileId,
            new DateTime(2026, 6, 1),
            new DateTime(2026, 6, 30),
            2_000_000m);

        await _f.Service.UploadReportAsync(
            targetA.Id,
            TargetReportServiceFixture.BuildRequest([(1, 200_000m)]),
            _f.AdminUserId,
            "Admin User");
        var targetBReport = await _f.Service.UploadReportAsync(
            targetB.Id,
            TargetReportServiceFixture.BuildRequest([(1, 350_000m)]),
            _f.AdminUserId,
            "Admin User");

        await _f.Service.UploadReportAsync(
            targetA.Id,
            TargetReportServiceFixture.BuildRequest([(1, 200_000m), (10, 250_000m)]),
            _f.AdminUserId,
            "Admin User");

        var currentA = await _f.Service.GetCurrentReportAsync(targetA.Id);
        var currentB = await _f.Service.GetCurrentReportAsync(targetB.Id);
        var historyA = await _f.Service.GetHistoryAsync(targetA.Id);
        var historyB = await _f.Service.GetHistoryAsync(targetB.Id);
        var repHistoryB = await _f.Service.GetRepHistoryAsync(targetB.Id, _f.RepAUserId);
        var repDetailB = await _f.Service.GetRepReportByIdAsync(targetBReport.Id, _f.RepAUserId);

        currentA.ActualSales.Should().Be(450_000m);
        currentB.ActualSales.Should().Be(350_000m);
        historyA.Should().HaveCount(2).And.OnlyContain(report => report.TargetId == targetA.Id);
        historyB.Should().ContainSingle().Which.TargetId.Should().Be(targetB.Id);
        repHistoryB.Should().ContainSingle().Which.Id.Should().Be(targetBReport.Id);
        repDetailB.TargetId.Should().Be(targetB.Id);

        var targets = await _f.Db.Set<SalesTarget>()
            .Where(target => target.Id == targetA.Id || target.Id == targetB.Id)
            .ToListAsync();
        targets.Sum(target => target.TargetAmount).Should().Be(3_000_000m);
        targets.Sum(target => target.AchievedAmount).Should().Be(800_000m);
    }

    [Fact]
    public async Task Rep_CannotRead_AnotherRepsHistoricalReport()
    {
        var target = await _f.CreateTargetAsync(
            _f.RepAProfileId,
            new DateTime(2026, 6, 1),
            new DateTime(2026, 6, 30),
            1_000_000m);
        var report = await _f.Service.UploadReportAsync(
            target.Id,
            TargetReportServiceFixture.BuildRequest([(1, 200_000m)]),
            _f.AdminUserId,
            "Admin User");

        var historyAct = async () =>
            await _f.Service.GetRepHistoryAsync(target.Id, _f.RepBUserId);
        var detailAct = async () =>
            await _f.Service.GetRepReportByIdAsync(report.Id, _f.RepBUserId);

        await historyAct.Should().ThrowAsync<NotFoundException>();
        await detailAct.Should().ThrowAsync<NotFoundException>();
    }
}
