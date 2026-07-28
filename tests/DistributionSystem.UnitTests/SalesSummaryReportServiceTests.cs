using DistributionSystem.Application.DTOs.SalesSummary;
using DistributionSystem.Application.Exceptions;
using DistributionSystem.Application.Services.Implementations;
using DistributionSystem.Domain.Entities;
using DistributionSystem.Infrastructure.Data;
using DistributionSystem.Infrastructure.Data.Repositories.Implementations;
using DistributionSystem.Infrastructure.Data.Repositories.Interfaces;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace DistributionSystem.UnitTests;

public class SalesSummaryReportServiceFixture : IDisposable
{
    public ApplicationDbContext Db { get; }
    public IUnitOfWork Uow { get; }
    public SalesSummaryReportService Service { get; }

    public Guid RegionAId = Guid.NewGuid();
    public Guid RegionBId = Guid.NewGuid();
    public Guid RepAUserId = Guid.NewGuid();
    public Guid RepAProfileId = Guid.NewGuid();

    public SalesSummaryReportServiceFixture()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        Db = new ApplicationDbContext(options);
        Uow = new UnitOfWork(Db);
        Service = new SalesSummaryReportService(Uow);

        SeedAsync().GetAwaiter().GetResult();
    }

    private async Task SeedAsync()
    {
        Db.Regions.AddRange(
            new Region { Id = RegionAId, Name = "Region A" },
            new Region { Id = RegionBId, Name = "Region B" });

        Db.SalesRepProfiles.Add(new SalesRepProfile
        {
            Id = RepAProfileId,
            UserId = RepAUserId,
            FullName = "Rep A",
            EmployeeCode = "RA1",
        });

        // Rep A is only assigned to Region A.
        Db.Set<RepRegion>().Add(new RepRegion { RepId = RepAProfileId, RegionId = RegionAId });

        await Db.SaveChangesAsync();
    }

    public void Dispose() => Db.Dispose();

    public UploadSalesSummaryRequest BuildRequest(Guid regionId, string periodFromStr = "2026-06-01", string periodToStr = "2026-06-30") => new()
    {
        RegionId = regionId,
        PeriodFrom = DateTime.Parse(periodFromStr).ToUniversalTime(),
        PeriodTo = DateTime.Parse(periodToStr).ToUniversalTime(),
        OriginalFileName = "CENTRAL RD 25.06.2026.xlsx",
        Entries =
        [
            new SalesSummaryEntryRequest { GroupName = "2026.06.01", SalesWithTax = 2301027.35m, Tax = 322932.92m, NetSales = 1978094.43m, Discount = 25428.41m, GrossSales = 2003522.84m, IsTotal = false, SortOrder = 0 },
            new SalesSummaryEntryRequest { GroupName = "2026.06.02", SalesWithTax = 1430686.35m, Tax = 207759.64m, NetSales = 1222926.71m, Discount = 25568.13m, GrossSales = 1248494.86m, IsTotal = false, SortOrder = 1 },
            new SalesSummaryEntryRequest { GroupName = "", SalesWithTax = 3731713.70m, Tax = 530692.56m, NetSales = 3201021.14m, Discount = 50996.54m, GrossSales = 3252017.70m, IsTotal = true, SortOrder = 2 },
        ],
    };
}

public class SalesSummaryUploadTests : IClassFixture<SalesSummaryReportServiceFixture>
{
    private readonly SalesSummaryReportServiceFixture _f;
    public SalesSummaryUploadTests(SalesSummaryReportServiceFixture f) => _f = f;

    [Fact]
    public async Task Upload_PersistsHeaderAndEntries_WithTotalsFromTotalRow()
    {
        var result = await _f.Service.UploadReportAsync(_f.BuildRequest(_f.RegionAId), "Admin User");

        result.RegionId.Should().Be(_f.RegionAId);
        result.RowCount.Should().Be(2); // total row excluded from RowCount
        result.TotalGrossSales.Should().Be(3252017.70m);
        result.OriginalFileName.Should().Be("CENTRAL RD 25.06.2026.xlsx");
    }

    [Fact]
    public async Task Upload_SameRegionAndPeriod_ReplacesExistingReport_NoDuplicate()
    {
        await _f.Service.UploadReportAsync(_f.BuildRequest(_f.RegionAId), "Admin User");
        var second = await _f.Service.UploadReportAsync(_f.BuildRequest(_f.RegionAId), "Admin User");

        var all = await _f.Service.GetAllReportsAsync();
        all.Count(r => r.RegionId == _f.RegionAId).Should().Be(1);
        all.Single(r => r.RegionId == _f.RegionAId).Id.Should().Be(second.Id);
    }

    [Fact]
    public async Task Upload_SameRegionDifferentPeriod_CreatesSeparateReport()
    {
        await _f.Service.UploadReportAsync(_f.BuildRequest(_f.RegionAId, "2026-06-01", "2026-06-30"), "Admin User");
        await _f.Service.UploadReportAsync(_f.BuildRequest(_f.RegionAId, "2026-07-01", "2026-07-31"), "Admin User");

        var all = await _f.Service.GetAllReportsAsync();
        all.Count(r => r.RegionId == _f.RegionAId).Should().Be(2);
    }
}

public class SalesSummaryAccessTests : IClassFixture<SalesSummaryReportServiceFixture>
{
    private readonly SalesSummaryReportServiceFixture _f;
    public SalesSummaryAccessTests(SalesSummaryReportServiceFixture f) => _f = f;

    [Fact]
    public async Task Rep_CanAccess_ReportForAssignedRegion()
    {
        var report = await _f.Service.UploadReportAsync(_f.BuildRequest(_f.RegionAId), "Admin User");

        var repReports = await _f.Service.GetRepReportsAsync(_f.RepAUserId);
        repReports.Should().Contain(r => r.Id == report.Id);

        var detail = await _f.Service.GetRepReportByIdAsync(report.Id, _f.RepAUserId);
        detail.RegionId.Should().Be(_f.RegionAId);
    }

    [Fact]
    public async Task Rep_CannotAccess_ReportForUnassignedRegion()
    {
        var report = await _f.Service.UploadReportAsync(_f.BuildRequest(_f.RegionBId), "Admin User");

        var repReports = await _f.Service.GetRepReportsAsync(_f.RepAUserId);
        repReports.Should().NotContain(r => r.Id == report.Id);

        var act = async () => await _f.Service.GetRepReportByIdAsync(report.Id, _f.RepAUserId);
        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Delete_RemovesReportAndEntries()
    {
        var report = await _f.Service.UploadReportAsync(_f.BuildRequest(_f.RegionBId, "2026-08-01", "2026-08-31"), "Admin User");

        await _f.Service.DeleteReportAsync(report.Id);

        var all = await _f.Service.GetAllReportsAsync();
        all.Should().NotContain(r => r.Id == report.Id);
    }
}
