using DistributionSystem.Application.Exceptions;
using DistributionSystem.Application.Services.Implementations;
using DistributionSystem.Domain.Entities;
using DistributionSystem.Infrastructure.Data;
using DistributionSystem.Infrastructure.Data.Repositories.Implementations;
using DistributionSystem.Infrastructure.Data.Repositories.Interfaces;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace DistributionSystem.UnitTests;

/// <summary>
/// Builds raw A–F column dictionaries the same shape MiniExcel hands back from
/// `stream.Query(useHeaderRow: false)`, so the pure row-classification logic
/// (StockReportService.ParseRows) can be exercised without round-tripping real .xlsx bytes.
/// </summary>
internal static class StockRowBuilder
{
    public static Dictionary<string, object?> Row(
        string? a = null, string? b = null, string? c = null, string? d = null, string? e = null, string? f = null)
        => new() { ["A"] = a, ["B"] = b, ["C"] = c, ["D"] = d, ["E"] = e, ["F"] = f };

    /// <summary>
    /// Mirrors the confirmed layout of the real sample workbook: 4 metadata rows, a blank
    /// separator, the header row, a leading blank row after the header (must be skipped), two
    /// GroupHeader→Item→GroupSubtotal blocks, two consecutive blank rows before the total (must
    /// collapse into one), the GrandTotal row, and two trailing blank rows (must be trimmed).
    /// </summary>
    public static List<IDictionary<string, object?>> ValidSample() =>
    [
        Row(a: "JANASIRI DISTRIBUTORS (PVT) LTD"),
        Row(a: "STOCK SUMMARY - DELMEGE FORSYTH & CO LTD / LIMIT 2,500,000.00"),
        Row(a: "DATE AS OF  : 23-07-2026"),
        Row(a: "Export Date and Time 24-07-2026 09:39:29 PM"),
        Row(),
        Row(a: "GroupName", b: "Item", c: "SalesDescription", d: "Cost (Ex vat)", e: "OnHand", f: "Amount"),
        Row(), // leading blank after header — must be skipped
        Row(a: "Group A"), // GroupHeader
        Row(a: "Group A", b: "ITM1", c: "Desc 1", d: "100.50", e: "10", f: "1005.00"), // Item
        Row(e: "10", f: "1005.00"), // GroupSubtotal
        Row(a: "Group B"), // GroupHeader
        Row(a: "Group B", b: "ITM2", c: "Desc 2", d: "50.25", e: "0", f: "0"), // Item
        Row(e: "0", f: "0"), // GroupSubtotal
        Row(), // blank
        Row(), // consecutive blank — must collapse with the one above into a single Blank row
        Row(b: "Total", e: "10", f: "1005.00"), // GrandTotal
        Row(), // trailing blank — must be trimmed
        Row(), // trailing blank — must be trimmed
    ];
}

public class StockReportParsingTests
{
    [Fact]
    public void ParseRows_ValidSample_ProducesExpectedRowTypesAndOrder()
    {
        var (header, rows) = StockReportService.ParseRows(StockRowBuilder.ValidSample());

        header.CompanyName.Should().Be("JANASIRI DISTRIBUTORS (PVT) LTD");
        header.ReportTitle.Should().Be("STOCK SUMMARY - DELMEGE FORSYTH & CO LTD / LIMIT 2,500,000.00");
        header.DateAsOf.Should().Be(new DateOnly(2026, 7, 23));
        header.ExportedAt.Should().Be(new DateTimeOffset(2026, 7, 24, 21, 39, 29, TimeSpan.FromHours(5.5)));
        header.TotalOnHand.Should().Be(10m);
        header.TotalAmount.Should().Be(1005.00m);

        // Leading blank after header skipped, the two consecutive blanks before Total collapsed to one,
        // trailing blanks after Total trimmed entirely.
        rows.Select(r => r.RowType).Should().Equal(
            StockReportRowType.GroupHeader,
            StockReportRowType.Item,
            StockReportRowType.GroupSubtotal,
            StockReportRowType.GroupHeader,
            StockReportRowType.Item,
            StockReportRowType.GroupSubtotal,
            StockReportRowType.Blank,
            StockReportRowType.GrandTotal);

        rows.Select(r => r.SortOrder).Should().Equal(Enumerable.Range(0, rows.Count));

        var item1 = rows[1];
        item1.GroupName.Should().Be("Group A");
        item1.Item.Should().Be("ITM1");
        item1.SalesDescription.Should().Be("Desc 1");
        item1.CostExVat.Should().Be(100.50m);
        item1.OnHand.Should().Be(10m);
        item1.Amount.Should().Be(1005.00m);

        var grandTotal = rows[^1];
        grandTotal.RowType.Should().Be(StockReportRowType.GrandTotal);
        grandTotal.OnHand.Should().Be(10m);
        grandTotal.Amount.Should().Be(1005.00m);
    }

    [Fact]
    public void ParseRows_RowCount_CountsItemRowsOnly()
    {
        var (_, rows) = StockReportService.ParseRows(StockRowBuilder.ValidSample());

        rows.Count(r => r.RowType == StockReportRowType.Item).Should().Be(2);
    }

    [Fact]
    public void ParseRows_MissingGroupNameHeader_Throws()
    {
        var raw = new List<IDictionary<string, object?>>
        {
            StockRowBuilder.Row(a: "Some Company"),
            StockRowBuilder.Row(a: "Some Title"),
        };

        var act = () => StockReportService.ParseRows(raw);

        act.Should().Throw<BusinessException>()
            .WithMessage("*GroupName*header row*");
    }

    [Fact]
    public void ParseRows_WrongColumnInHeaderRow_Throws()
    {
        var raw = new List<IDictionary<string, object?>>
        {
            StockRowBuilder.Row(a: "GroupName", b: "WrongColumn", c: "SalesDescription", d: "Cost (Ex vat)", e: "OnHand", f: "Amount"),
        };

        var act = () => StockReportService.ParseRows(raw);

        act.Should().Throw<BusinessException>()
            .WithMessage("*\"Item\"*position 2*");
    }

    [Fact]
    public void ParseRows_MalformedNumericCell_ThrowsWithRowAndColumn()
    {
        var raw = new List<IDictionary<string, object?>>(StockRowBuilder.ValidSample());
        // Row index 8 (Excel row 9, 1-based) is the "Group A" item row — corrupt its Cost cell.
        raw[8] = StockRowBuilder.Row(a: "Group A", b: "ITM1", c: "Desc 1", d: "N/A", e: "10", f: "1005.00");

        var act = () => StockReportService.ParseRows(raw);

        act.Should().Throw<BusinessException>()
            .WithMessage("*\"N/A\"*\"Cost (Ex vat)\"*row 9*");
    }

    [Fact]
    public void ParseRows_MissingGrandTotal_Throws()
    {
        var raw = StockRowBuilder.ValidSample();
        raw.RemoveAt(raw.Count - 3); // remove the GrandTotal row (third from the end)

        var act = () => StockReportService.ParseRows(raw);

        act.Should().Throw<BusinessException>()
            .WithMessage("Grand Total row was not found. Please upload a complete Stock Summary Excel file.");
    }

    [Fact]
    public void ParseRows_EmptyFile_Throws()
    {
        var act = () => StockReportService.ParseRows([]);

        act.Should().Throw<BusinessException>()
            .WithMessage("*empty*");
    }
}

public class StockReportServiceFixture : IDisposable
{
    public ApplicationDbContext Db { get; }
    public IUnitOfWork Uow { get; }
    public StockReportService Service { get; }

    public Guid RegionAId = Guid.NewGuid();
    public Guid RegionBId = Guid.NewGuid();
    public Guid RepAUserId = Guid.NewGuid();
    public Guid RepAProfileId = Guid.NewGuid();

    public StockReportServiceFixture()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        Db = new ApplicationDbContext(options);
        Uow = new UnitOfWork(Db);
        Service = new StockReportService(Uow);

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

    /// <summary>Fresh parsed header + rows each call — PersistReportAsync mutates the row instances.</summary>
    internal (StockReportService.ParsedHeader header, List<StockReportRow> rows) BuildValidParsedReport() =>
        StockReportService.ParseRows(StockRowBuilder.ValidSample());
}

public class StockReportUploadTests : IClassFixture<StockReportServiceFixture>
{
    private readonly StockReportServiceFixture _f;
    public StockReportUploadTests(StockReportServiceFixture f) => _f = f;

    [Fact]
    public async Task Upload_PersistsHeaderAndRows_WithTotalsFromGrandTotalRow()
    {
        var (header, rows) = _f.BuildValidParsedReport();

        var result = await _f.Service.PersistReportAsync(_f.RegionAId, header, rows, "sample.xlsx", "Admin User");

        result.RegionId.Should().Be(_f.RegionAId);
        result.RowCount.Should().Be(2); // Item rows only
        result.TotalOnHand.Should().Be(10m);
        result.TotalAmount.Should().Be(1005.00m);

        var detail = await _f.Service.GetReportByRegionAsync(_f.RegionAId);
        detail.Rows.Should().HaveCount(8);
        detail.Rows.Select(r => r.SortOrder).Should().Equal(Enumerable.Range(0, 8));
    }

    [Fact]
    public async Task Upload_SameRegion_ReplacesExistingReport_NoDuplicate()
    {
        var (h1, r1) = _f.BuildValidParsedReport();
        await _f.Service.PersistReportAsync(_f.RegionBId, h1, r1, "first.xlsx", "Admin User");

        var (h2, r2) = _f.BuildValidParsedReport();
        var second = await _f.Service.PersistReportAsync(_f.RegionBId, h2, r2, "second.xlsx", "Admin User");

        var all = await _f.Service.GetAllReportsAsync();
        all.Count(r => r.RegionId == _f.RegionBId).Should().Be(1);
        all.Single(r => r.RegionId == _f.RegionBId).Id.Should().Be(second.Id);
    }

    [Fact]
    public async Task Upload_UnknownRegion_ThrowsNotFound()
    {
        var (header, rows) = _f.BuildValidParsedReport();

        var act = async () => await _f.Service.PersistReportAsync(Guid.NewGuid(), header, rows, "sample.xlsx", "Admin User");

        await act.Should().ThrowAsync<NotFoundException>();
    }
}

public class StockReportAccessTests : IClassFixture<StockReportServiceFixture>
{
    private readonly StockReportServiceFixture _f;
    public StockReportAccessTests(StockReportServiceFixture f) => _f = f;

    [Fact]
    public async Task Rep_CanAccess_ReportForAssignedRegion()
    {
        var (header, rows) = _f.BuildValidParsedReport();
        var report = await _f.Service.PersistReportAsync(_f.RegionAId, header, rows, "sample.xlsx", "Admin User");

        var repReports = await _f.Service.GetRepReportsAsync(_f.RepAUserId);
        repReports.Should().Contain(r => r.Id == report.Id);

        var detail = await _f.Service.GetRepReportByRegionAsync(_f.RepAUserId, _f.RegionAId);
        detail.RegionId.Should().Be(_f.RegionAId);
    }

    [Fact]
    public async Task Rep_CannotAccess_ReportForUnassignedRegion()
    {
        var (header, rows) = _f.BuildValidParsedReport();
        var report = await _f.Service.PersistReportAsync(_f.RegionBId, header, rows, "sample.xlsx", "Admin User");

        var repReports = await _f.Service.GetRepReportsAsync(_f.RepAUserId);
        repReports.Should().NotContain(r => r.Id == report.Id);

        var act = async () => await _f.Service.GetRepReportByRegionAsync(_f.RepAUserId, _f.RegionBId);
        await act.Should().ThrowAsync<ForbiddenException>();
    }

    [Fact]
    public async Task Delete_RemovesReportAndRows()
    {
        var (header, rows) = _f.BuildValidParsedReport();
        var uniqueRegion = Guid.NewGuid();
        _f.Db.Regions.Add(new Region { Id = uniqueRegion, Name = "Region C" });
        await _f.Db.SaveChangesAsync();

        var report = await _f.Service.PersistReportAsync(uniqueRegion, header, rows, "sample.xlsx", "Admin User");

        await _f.Service.DeleteReportAsync(uniqueRegion);

        var all = await _f.Service.GetAllReportsAsync();
        all.Should().NotContain(r => r.Id == report.Id);
    }
}
