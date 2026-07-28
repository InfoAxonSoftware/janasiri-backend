using System.Globalization;
using DistributionSystem.Application.DTOs.Stock;
using DistributionSystem.Application.Exceptions;
using DistributionSystem.Application.Services.Interfaces;
using DistributionSystem.Domain.Entities;
using DistributionSystem.Infrastructure.Data.Repositories.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using MiniExcelLibs;

namespace DistributionSystem.Application.Services.Implementations;

public class StockReportService : IStockReportService
{
    private static readonly string[] ExpectedHeaders =
        ["GroupName", "Item", "SalesDescription", "Cost (Ex vat)", "OnHand", "Amount"];

    private static readonly string[] ColumnLetters = ["A", "B", "C", "D", "E", "F"];

    /// <summary>Sri Lanka standard time offset — the "Export Date and Time" line has no timezone marker.</summary>
    private static readonly TimeSpan SriLankaOffset = new(5, 30, 0);

    private readonly IUnitOfWork _unitOfWork;

    public StockReportService(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    // ── Upload ────────────────────────────────────────────────────────────────

    public async Task<StockReportSummaryDto> UploadReportAsync(
        Guid regionId, IFormFile file, string uploadedBy, CancellationToken ct = default)
    {
        using var stream = file.OpenReadStream();
        var (parsed, rows) = ParseExcel(stream);

        return await PersistReportAsync(regionId, parsed, rows, file.FileName, uploadedBy, ct);
    }

    /// <summary>
    /// The replace-existing-report / insert-new-report / insert-rows transaction, split out from
    /// the file-parsing step above so it can be unit-tested against already-parsed rows directly.
    /// </summary>
    internal async Task<StockReportSummaryDto> PersistReportAsync(
        Guid regionId, ParsedHeader parsed, List<StockReportRow> rows, string? originalFileName, string uploadedBy, CancellationToken ct = default)
    {
        var region = await _unitOfWork.Repository<Region>().Query()
            .FirstOrDefaultAsync(r => r.Id == regionId, ct)
            ?? throw new NotFoundException("Region", regionId);

        await _unitOfWork.BeginTransactionAsync(ct);
        try
        {
            var existing = await _unitOfWork.Repository<StockReport>().Query()
                .Include(r => r.Rows)
                .FirstOrDefaultAsync(r => r.RegionId == regionId, ct);

            if (existing != null)
            {
                foreach (var row in existing.Rows.ToList())
                    _unitOfWork.Repository<StockReportRow>().Remove(row);
                _unitOfWork.Repository<StockReport>().Remove(existing);
                await _unitOfWork.SaveChangesAsync(ct);
            }

            var report = new StockReport
            {
                RegionId = regionId,
                RegionName = region.Name,
                CompanyName = parsed.CompanyName,
                ReportTitle = parsed.ReportTitle,
                DateAsOf = parsed.DateAsOf,
                ExportedAt = parsed.ExportedAt,
                OriginalFileName = originalFileName,
                RowCount = rows.Count(r => r.RowType == StockReportRowType.Item),
                TotalOnHand = parsed.TotalOnHand,
                TotalAmount = parsed.TotalAmount,
                UploadedAt = DateTime.UtcNow,
                UploadedBy = uploadedBy,
            };

            await _unitOfWork.Repository<StockReport>().AddAsync(report, ct);
            await _unitOfWork.SaveChangesAsync(ct);

            foreach (var row in rows)
            {
                row.StockReportId = report.Id;
                await _unitOfWork.Repository<StockReportRow>().AddAsync(row, ct);
            }
            await _unitOfWork.SaveChangesAsync(ct);

            await _unitOfWork.CommitTransactionAsync(ct);

            return MapToSummaryDto(report);
        }
        catch
        {
            await _unitOfWork.RollbackTransactionAsync(ct);
            throw;
        }
    }

    // ── Admin queries ─────────────────────────────────────────────────────────

    public async Task<List<StockReportSummaryDto>> GetAllReportsAsync(CancellationToken ct = default)
    {
        var reports = await _unitOfWork.Repository<StockReport>().Query()
            .OrderBy(r => r.RegionName)
            .ToListAsync(ct);

        return reports.Select(MapToSummaryDto).ToList();
    }

    public async Task<StockReportDetailDto> GetReportByRegionAsync(Guid regionId, CancellationToken ct = default)
    {
        var report = await _unitOfWork.Repository<StockReport>().Query()
            .Include(r => r.Rows.OrderBy(row => row.SortOrder))
            .FirstOrDefaultAsync(r => r.RegionId == regionId, ct)
            ?? throw new NotFoundException("Stock report for region", regionId);

        return MapToDetailDto(report);
    }

    public async Task DeleteReportAsync(Guid regionId, CancellationToken ct = default)
    {
        var report = await _unitOfWork.Repository<StockReport>().Query()
            .Include(r => r.Rows)
            .FirstOrDefaultAsync(r => r.RegionId == regionId, ct)
            ?? throw new NotFoundException("Stock report for region", regionId);

        foreach (var row in report.Rows.ToList())
            _unitOfWork.Repository<StockReportRow>().Remove(row);
        _unitOfWork.Repository<StockReport>().Remove(report);
        await _unitOfWork.SaveChangesAsync(ct);
    }

    // ── Rep queries ───────────────────────────────────────────────────────────

    public async Task<List<StockReportSummaryDto>> GetRepReportsAsync(Guid repUserId, CancellationToken ct = default)
    {
        var regionIds = await GetAssignedRegionIdsAsync(repUserId, ct);

        var reports = await _unitOfWork.Repository<StockReport>().Query()
            .Where(r => regionIds.Contains(r.RegionId))
            .OrderBy(r => r.RegionName)
            .ToListAsync(ct);

        return reports.Select(MapToSummaryDto).ToList();
    }

    public async Task<StockReportDetailDto> GetRepReportByRegionAsync(Guid repUserId, Guid regionId, CancellationToken ct = default)
    {
        var regionIds = await GetAssignedRegionIdsAsync(repUserId, ct);
        if (!regionIds.Contains(regionId))
            throw new ForbiddenException("You are not assigned to this region.");

        var report = await _unitOfWork.Repository<StockReport>().Query()
            .Include(r => r.Rows.OrderBy(row => row.SortOrder))
            .FirstOrDefaultAsync(r => r.RegionId == regionId, ct)
            ?? throw new NotFoundException("Stock report for region", regionId);

        return MapToDetailDto(report);
    }

    private async Task<List<Guid>> GetAssignedRegionIdsAsync(Guid repUserId, CancellationToken ct)
    {
        var rep = await _unitOfWork.Repository<SalesRepProfile>().Query()
            .Include(r => r.Regions)
            .FirstOrDefaultAsync(r => r.UserId == repUserId, ct)
            ?? throw new NotFoundException("Sales rep profile", repUserId);

        return rep.Regions.Select(rr => rr.RegionId).ToList();
    }

    // ── Excel parsing ─────────────────────────────────────────────────────────

    internal sealed class ParsedHeader
    {
        public string CompanyName { get; set; } = string.Empty;
        public string ReportTitle { get; set; } = string.Empty;
        public DateOnly? DateAsOf { get; set; }
        public DateTimeOffset? ExportedAt { get; set; }
        public decimal TotalOnHand { get; set; }
        public decimal TotalAmount { get; set; }
    }

    /// <summary>
    /// Parses the Stock Summary Excel export.
    ///
    /// Expects: metadata rows, a blank separator, a header row containing GroupName/Item/
    /// SalesDescription/Cost (Ex vat)/OnHand/Amount, then repeating GroupHeader → Item(s) →
    /// GroupSubtotal blocks, and a GrandTotal row ("Total" in the Item column). Row order is
    /// preserved via SortOrder; malformed numeric cells and a missing header/grand-total row
    /// are rejected rather than silently accepted.
    /// </summary>
    private static (ParsedHeader header, List<StockReportRow> rows) ParseExcel(Stream stream)
    {
        var allRows = stream.Query(useHeaderRow: false).Cast<IDictionary<string, object?>>().ToList();
        return ParseRows(allRows);
    }

    /// <summary>
    /// Pure row-classification logic, separated from the MiniExcel I/O layer above so it can be
    /// exercised directly in unit tests without needing to round-trip real .xlsx bytes.
    /// </summary>
    internal static (ParsedHeader header, List<StockReportRow> rows) ParseRows(List<IDictionary<string, object?>> allRows)
    {
        if (allRows.Count == 0)
            throw new BusinessException("The uploaded file is empty.");

        static string? Cell(IDictionary<string, object?> row, string col) =>
            row.TryGetValue(col, out var v) && v != null ? v.ToString()!.Trim() : null;

        // ── 1. Locate the header row by finding "GroupName" ─────────────────────
        int headerIdx = -1;
        for (int i = 0; i < Math.Min(allRows.Count, 20); i++)
        {
            if (string.Equals(Cell(allRows[i], "A"), "GroupName", StringComparison.OrdinalIgnoreCase))
            {
                headerIdx = i;
                break;
            }
        }
        if (headerIdx < 0)
            throw new BusinessException("Unsupported file: could not find the \"GroupName\" header row.");

        // ── 2. Validate all six expected columns are present in order ───────────
        var headerRow = allRows[headerIdx];
        for (int c = 0; c < ExpectedHeaders.Length; c++)
        {
            var actual = Cell(headerRow, ColumnLetters[c]) ?? string.Empty;
            if (!string.Equals(actual, ExpectedHeaders[c], StringComparison.OrdinalIgnoreCase))
                throw new BusinessException(
                    $"Unsupported file: expected column \"{ExpectedHeaders[c]}\" at position {c + 1} but found \"{actual}\".");
        }

        // ── 3. Parse metadata rows above the header ──────────────────────────────
        var header = new ParsedHeader();
        var metaLines = new List<string>();
        for (int i = 0; i < headerIdx; i++)
        {
            var text = Cell(allRows[i], "A");
            if (!string.IsNullOrWhiteSpace(text)) metaLines.Add(text);
        }
        if (metaLines.Count > 0) header.CompanyName = metaLines[0];
        if (metaLines.Count > 1) header.ReportTitle = metaLines[1];
        foreach (var line in metaLines)
        {
            if (line.StartsWith("DATE AS OF", StringComparison.OrdinalIgnoreCase))
            {
                var idx = line.IndexOf(':');
                var datePart = (idx >= 0 ? line[(idx + 1)..] : line).Trim();
                if (DateOnly.TryParseExact(datePart, "dd-MM-yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
                    header.DateAsOf = d;
            }
            else if (line.StartsWith("Export Date and Time", StringComparison.OrdinalIgnoreCase))
            {
                var rest = line["Export Date and Time".Length..].Trim();
                if (DateTime.TryParseExact(rest, "dd-MM-yyyy hh:mm:ss tt", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
                    header.ExportedAt = new DateTimeOffset(DateTime.SpecifyKind(dt, DateTimeKind.Unspecified), SriLankaOffset);
            }
        }

        // ── 4. Classify every row after the header ───────────────────────────────
        decimal? ParseDecimalStrict(string? raw, int excelRowNumber, string columnName)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;
            var s = raw.Replace(",", "").Trim();
            if (decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var d)) return d;
            throw new BusinessException(
                $"Invalid numeric value \"{raw}\" in column \"{columnName}\" at Excel row {excelRowNumber}.");
        }

        var classified = new List<StockReportRow>();
        for (int i = headerIdx + 1; i < allRows.Count; i++)
        {
            var row = allRows[i];
            int excelRowNumber = i + 1;

            var a = Cell(row, "A");
            var b = Cell(row, "B");
            var c = Cell(row, "C");
            var cost = ParseDecimalStrict(Cell(row, "D"), excelRowNumber, "Cost (Ex vat)");
            var onHand = ParseDecimalStrict(Cell(row, "E"), excelRowNumber, "OnHand");
            var amount = ParseDecimalStrict(Cell(row, "F"), excelRowNumber, "Amount");

            bool allBlank = string.IsNullOrEmpty(a) && string.IsNullOrEmpty(b) && string.IsNullOrEmpty(c)
                && cost == null && onHand == null && amount == null;

            StockReportRowType type;
            if (allBlank)
            {
                type = StockReportRowType.Blank;
            }
            else if (string.Equals(b, "Total", StringComparison.OrdinalIgnoreCase)
                && string.IsNullOrEmpty(a) && string.IsNullOrEmpty(c) && cost == null)
            {
                type = StockReportRowType.GrandTotal;
            }
            else if (string.IsNullOrEmpty(a) && string.IsNullOrEmpty(b) && string.IsNullOrEmpty(c) && cost == null
                && (onHand != null || amount != null))
            {
                type = StockReportRowType.GroupSubtotal;
            }
            else if (!string.IsNullOrEmpty(a) && string.IsNullOrEmpty(b) && string.IsNullOrEmpty(c)
                && cost == null && onHand == null && amount == null)
            {
                type = StockReportRowType.GroupHeader;
            }
            else
            {
                type = StockReportRowType.Item;
            }

            classified.Add(new StockReportRow
            {
                RowType = type,
                GroupName = type is StockReportRowType.Blank or StockReportRowType.GroupSubtotal or StockReportRowType.GrandTotal ? null : a,
                Item = type == StockReportRowType.Item ? b : (type == StockReportRowType.GrandTotal ? b : null),
                SalesDescription = type == StockReportRowType.Item ? c : null,
                CostExVat = type == StockReportRowType.Item ? cost : null,
                OnHand = type is StockReportRowType.Item or StockReportRowType.GroupSubtotal or StockReportRowType.GrandTotal ? onHand : null,
                Amount = type is StockReportRowType.Item or StockReportRowType.GroupSubtotal or StockReportRowType.GrandTotal ? amount : null,
            });
        }

        // ── 5. Trim leading/trailing blanks and collapse interior blank runs ─────
        int start = 0;
        while (start < classified.Count && classified[start].RowType == StockReportRowType.Blank) start++;

        int grandTotalIdx = classified.FindLastIndex(r => r.RowType == StockReportRowType.GrandTotal);
        if (grandTotalIdx < 0)
            throw new BusinessException("Grand Total row was not found. Please upload a complete Stock Summary Excel file.");

        int end = grandTotalIdx; // exclude anything after the grand total row (trailing blanks)

        var trimmed = new List<StockReportRow>();
        for (int i = start; i <= end; i++)
        {
            if (classified[i].RowType == StockReportRowType.Blank
                && trimmed.Count > 0 && trimmed[^1].RowType == StockReportRowType.Blank)
                continue; // collapse consecutive blanks into one
            trimmed.Add(classified[i]);
        }

        int order = 0;
        foreach (var row in trimmed)
            row.SortOrder = order++;

        var grandTotal = trimmed.Last(r => r.RowType == StockReportRowType.GrandTotal);
        header.TotalOnHand = grandTotal.OnHand ?? 0m;
        header.TotalAmount = grandTotal.Amount ?? 0m;

        return (header, trimmed);
    }

    // ── Mappers ───────────────────────────────────────────────────────────────

    private static StockReportSummaryDto MapToSummaryDto(StockReport r) => new()
    {
        Id = r.Id,
        RegionId = r.RegionId,
        RegionName = r.RegionName,
        CompanyName = r.CompanyName,
        ReportTitle = r.ReportTitle,
        DateAsOf = r.DateAsOf,
        ExportedAt = r.ExportedAt,
        RowCount = r.RowCount,
        TotalOnHand = r.TotalOnHand,
        TotalAmount = r.TotalAmount,
        UploadedAt = r.UploadedAt,
        UploadedBy = r.UploadedBy,
    };

    private static StockReportDetailDto MapToDetailDto(StockReport r)
    {
        var dto = new StockReportDetailDto
        {
            Id = r.Id,
            RegionId = r.RegionId,
            RegionName = r.RegionName,
            CompanyName = r.CompanyName,
            ReportTitle = r.ReportTitle,
            DateAsOf = r.DateAsOf,
            ExportedAt = r.ExportedAt,
            RowCount = r.RowCount,
            TotalOnHand = r.TotalOnHand,
            TotalAmount = r.TotalAmount,
            UploadedAt = r.UploadedAt,
            UploadedBy = r.UploadedBy,
            Rows = r.Rows
                .OrderBy(row => row.SortOrder)
                .Select(row => new StockReportRowDto
                {
                    Id = row.Id,
                    RowType = row.RowType.ToString(),
                    SortOrder = row.SortOrder,
                    GroupName = row.GroupName,
                    Item = row.Item,
                    SalesDescription = row.SalesDescription,
                    CostExVat = row.CostExVat,
                    OnHand = row.OnHand,
                    Amount = row.Amount,
                })
                .ToList(),
        };
        return dto;
    }
}
