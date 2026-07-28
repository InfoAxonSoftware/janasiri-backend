using DistributionSystem.Application.DTOs.TargetReports;
using DistributionSystem.Application.Exceptions;
using DistributionSystem.Application.Services.Interfaces;
using DistributionSystem.Domain.Entities;
using DistributionSystem.Infrastructure.Data.Repositories.Interfaces;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace DistributionSystem.Application.Services.Implementations;

public class TargetReportService : ITargetReportService
{
    private readonly IUnitOfWork _unitOfWork;

    public TargetReportService(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<TargetReportSummaryDto> UploadReportAsync(
        Guid targetId, UploadTargetReportRequest request, Guid uploadedByUserId, string uploadedBy, CancellationToken ct = default)
    {
        var target = await _unitOfWork.Repository<SalesTarget>().Query()
            .FirstOrDefaultAsync(t => t.Id == targetId, ct)
            ?? throw new NotFoundException("Sales target", targetId);

        if (request.Entries.Count == 0)
            throw new BusinessException("No valid Invoice rows found in the file.", "NO_INVOICE_ROWS");

        var today = DateTime.UtcNow.Date;
        if (request.Entries.Any(e => e.TxnDate.Date > today))
            throw new BusinessException("The report contains invoice dates in the future. Please check the uploaded file.", "FUTURE_DATE");

        var distinctMonths = request.Entries.Select(e => new { e.TxnDate.Year, e.TxnDate.Month }).Distinct().ToList();
        if (distinctMonths.Count > 1)
            throw new BusinessException("The report contains invoice dates from more than one month. Upload a single month's report at a time.", "MULTIPLE_MONTHS");

        var fromDate = request.Entries.Min(e => e.TxnDate).Date;
        var asAtDate = request.Entries.Max(e => e.TxnDate).Date;
        // Use the exact grand-total value supplied by the source Excel row.
        // Fall back to the Invoice-row sum only for legacy clients/files that
        // do not contain a preserved source GrandTotal row.
        var sourceGrandTotal = request.SourceRows
            .OrderBy(r => r.SortOrder)
            .LastOrDefault(r =>
                string.Equals(r.RowType, "GrandTotal", StringComparison.OrdinalIgnoreCase)
                && r.SalesWithTax.HasValue);

        var actualSales = sourceGrandTotal?.SalesWithTax
            ?? request.Entries.Sum(e => e.SalesWithTax);
        var distinctOrderCount = request.Entries
            .Select(e => (e.RefNo ?? string.Empty).Trim())
            .Where(r => r.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
        var distinctCustomerCount = request.Entries
            .Select(e => NormalizeCustomerName(e.CustomerName))
            .Where(c => c.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

        if (fromDate < target.StartDate.Date || asAtDate > target.EndDate.Date)
        {
            throw new BusinessException(
                $"The report period ({fromDate:dd MMM yyyy} - {asAtDate:dd MMM yyyy}) does not belong to the selected target period ({target.StartDate:dd MMM yyyy} - {target.EndDate:dd MMM yyyy}).",
                "PERIOD_MISMATCH");
        }

        var existingCurrent = await _unitOfWork.Repository<TargetSalesReport>().Query()
            .FirstOrDefaultAsync(r => r.TargetId == targetId && r.IsCurrent, ct);

        if (existingCurrent != null && asAtDate < existingCurrent.AsAtDate.Date && !request.Force)
        {
            throw new BusinessException(
                $"This report's As At Date ({asAtDate:dd MMM yyyy}) is older than the current report ({existingCurrent.AsAtDate:dd MMM yyyy}). Confirm to replace it anyway.",
                "OLDER_REPORT_CONFIRMATION_REQUIRED");
        }

        if (existingCurrent != null)
        {
            existingCurrent.IsCurrent = false;
            await _unitOfWork.SaveChangesAsync(ct);
        }

        var report = new TargetSalesReport
        {
            TargetId = targetId,
            RepId = target.RepId,
            OriginalFileName = request.OriginalFileName,
            FromDate = fromDate,
            AsAtDate = asAtDate,
            ActualSales = actualSales,
            DistinctOrderCount = distinctOrderCount,
            DistinctCustomerCount = distinctCustomerCount,
            IsCurrent = true,
            UploadedAt = DateTime.UtcNow,
            UploadedByUserId = uploadedByUserId,
            UploadedBy = uploadedBy,
            SourceRowsJson = request.SourceRows.Count == 0
                ? null
                : JsonSerializer.Serialize(request.SourceRows.OrderBy(r => r.SortOrder).ToList()),
        };

        await _unitOfWork.Repository<TargetSalesReport>().AddAsync(report, ct);
        await _unitOfWork.SaveChangesAsync(ct);

        int order = 0;
        foreach (var req in request.Entries.OrderBy(e => e.SortOrder))
        {
            await _unitOfWork.Repository<TargetSalesReportEntry>().AddAsync(new TargetSalesReportEntry
            {
                TargetSalesReportId = report.Id,
                TxnDate = req.TxnDate,
                RefNo = req.RefNo,
                CustomerName = req.CustomerName,
                ItemDescription = req.ItemDescription,
                Qty = req.Qty,
                Discount = req.Discount,
                SalesWithTax = req.SalesWithTax,
                SortOrder = order++,
            }, ct);
        }
        await _unitOfWork.SaveChangesAsync(ct);

        // Replace (not add to) the target's actual-sales figure — cumulative "as at now" report.
        target.AchievedAmount = actualSales;
        target.UpdatedAt = DateTime.UtcNow;
        target.UpdatedBy = uploadedBy;
        await _unitOfWork.SaveChangesAsync(ct);

        return MapToSummary(report);
    }

    public async Task<List<TargetReportSummaryDto>> GetHistoryAsync(Guid targetId, CancellationToken ct = default)
    {
        var reports = await _unitOfWork.Repository<TargetSalesReport>().Query()
            .Where(r => r.TargetId == targetId)
            .OrderByDescending(r => r.UploadedAt)
            .ToListAsync(ct);

        return reports.Select(MapToSummary).ToList();
    }

    public async Task<TargetReportDetailDto> GetReportByIdAsync(Guid reportId, CancellationToken ct = default)
    {
        var report = await _unitOfWork.Repository<TargetSalesReport>().Query()
            .Include(r => r.Target).ThenInclude(t => t.Rep)
            .Include(r => r.Entries.OrderBy(e => e.SortOrder))
            .FirstOrDefaultAsync(r => r.Id == reportId, ct)
            ?? throw new NotFoundException("Target sales report", reportId);

        return MapToDetail(report);
    }

    public async Task<TargetReportDetailDto> GetCurrentReportAsync(Guid targetId, CancellationToken ct = default)
    {
        var report = await _unitOfWork.Repository<TargetSalesReport>().Query()
            .Include(r => r.Target).ThenInclude(t => t.Rep)
            .Include(r => r.Entries.OrderBy(e => e.SortOrder))
            .FirstOrDefaultAsync(r => r.TargetId == targetId && r.IsCurrent, ct)
            ?? throw new NotFoundException("Current target sales report", targetId);

        return MapToDetail(report);
    }

    public async Task<TargetReportDetailDto> GetRepCurrentReportAsync(Guid targetId, Guid repUserId, CancellationToken ct = default)
    {
        var rep = await _unitOfWork.Repository<SalesRepProfile>().Query()
            .FirstOrDefaultAsync(r => r.UserId == repUserId, ct)
            ?? throw new NotFoundException("Sales rep profile", repUserId);

        var target = await _unitOfWork.Repository<SalesTarget>().Query()
            .FirstOrDefaultAsync(t => t.Id == targetId && t.RepId == rep.Id, ct)
            ?? throw new NotFoundException("Sales target", targetId);

        return await GetCurrentReportAsync(target.Id, ct);
    }

    public async Task<List<TargetReportSummaryDto>> GetRepHistoryAsync(
        Guid targetId,
        Guid repUserId,
        CancellationToken ct = default)
    {
        var ownsTarget = await _unitOfWork.Repository<SalesTarget>().Query()
            .AnyAsync(target => target.Id == targetId && target.Rep.UserId == repUserId, ct);
        if (!ownsTarget)
            throw new NotFoundException("Sales target", targetId);

        return await GetHistoryAsync(targetId, ct);
    }

    public async Task<TargetReportDetailDto> GetRepReportByIdAsync(
        Guid reportId,
        Guid repUserId,
        CancellationToken ct = default)
    {
        var report = await _unitOfWork.Repository<TargetSalesReport>().Query()
            .Include(r => r.Target).ThenInclude(t => t.Rep)
            .Include(r => r.Entries.OrderBy(e => e.SortOrder))
            .FirstOrDefaultAsync(
                r => r.Id == reportId && r.Target.Rep.UserId == repUserId,
                ct)
            ?? throw new NotFoundException("Target sales report", reportId);

        return MapToDetail(report);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>Trim + collapse internal whitespace + case-insensitive compare, for distinct-customer counting only.</summary>
    private static string NormalizeCustomerName(string? name) =>
        string.IsNullOrWhiteSpace(name) ? string.Empty : System.Text.RegularExpressions.Regex.Replace(name.Trim(), @"\s+", " ");

    private static List<TargetReportSourceRowDto> DeserializeSourceRows(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return [];

        try
        {
            return JsonSerializer.Deserialize<List<TargetReportSourceRowDto>>(json)
                ?.OrderBy(r => r.SortOrder)
                .ToList() ?? [];
        }
        catch (JsonException)
        {
            // Historical reports created before exact-source preservation remain readable.
            return [];
        }
    }

    // ── Mappers ───────────────────────────────────────────────────────────────

    private static TargetReportSummaryDto MapToSummary(TargetSalesReport r) => new()
    {
        Id = r.Id,
        TargetId = r.TargetId,
        OriginalFileName = r.OriginalFileName,
        FromDate = r.FromDate,
        AsAtDate = r.AsAtDate,
        ActualSales = r.ActualSales,
        DistinctOrderCount = r.DistinctOrderCount,
        DistinctCustomerCount = r.DistinctCustomerCount,
        IsCurrent = r.IsCurrent,
        UploadedAt = r.UploadedAt,
        UploadedBy = r.UploadedBy,
    };

    private static TargetReportDetailDto MapToDetail(TargetSalesReport r) => new()
    {
        Id = r.Id,
        TargetId = r.TargetId,
        RepId = r.RepId,
        RepName = r.Target?.Rep?.FullName ?? string.Empty,
        TargetPeriod = r.Target?.TargetPeriod ?? string.Empty,
        TargetStartDate = r.Target?.StartDate ?? default,
        TargetEndDate = r.Target?.EndDate ?? default,
        TargetAmount = r.Target?.TargetAmount ?? 0,
        OriginalFileName = r.OriginalFileName,
        FromDate = r.FromDate,
        AsAtDate = r.AsAtDate,
        ActualSales = r.ActualSales,
        DistinctOrderCount = r.DistinctOrderCount,
        DistinctCustomerCount = r.DistinctCustomerCount,
        IsCurrent = r.IsCurrent,
        UploadedAt = r.UploadedAt,
        UploadedBy = r.UploadedBy,
        SourceRows = DeserializeSourceRows(r.SourceRowsJson),
        Entries = r.Entries
            .OrderBy(e => e.SortOrder)
            .Select(e => new TargetReportEntryDto
            {
                Id = e.Id,
                TxnDate = e.TxnDate,
                RefNo = e.RefNo,
                CustomerName = e.CustomerName,
                ItemDescription = e.ItemDescription,
                Qty = e.Qty,
                Discount = e.Discount,
                SalesWithTax = e.SalesWithTax,
                SortOrder = e.SortOrder,
            })
            .ToList(),
    };
}
