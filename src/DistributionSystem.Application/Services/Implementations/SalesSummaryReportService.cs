using DistributionSystem.Application.DTOs.SalesSummary;
using DistributionSystem.Application.Exceptions;
using DistributionSystem.Application.Services.Interfaces;
using DistributionSystem.Domain.Entities;
using DistributionSystem.Infrastructure.Data.Repositories.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace DistributionSystem.Application.Services.Implementations;

public class SalesSummaryReportService : ISalesSummaryReportService
{
    private readonly IUnitOfWork _unitOfWork;

    public SalesSummaryReportService(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    // ── Upload ────────────────────────────────────────────────────────────────

    public async Task<SalesSummaryReportDto> UploadReportAsync(UploadSalesSummaryRequest request, string uploadedBy, CancellationToken ct = default)
    {
        var region = await _unitOfWork.Repository<Region>().Query()
            .FirstOrDefaultAsync(r => r.Id == request.RegionId, ct)
            ?? throw new NotFoundException("Region", request.RegionId);

        // Duplicate-prevention: same region + reporting period → replace (delete then re-insert),
        // mirroring the existing Outstanding Reports "upload = replace" convention.
        var existing = await _unitOfWork.Repository<SalesSummaryReport>().Query()
            .Include(r => r.Entries)
            .FirstOrDefaultAsync(r => r.RegionId == request.RegionId
                && r.PeriodFrom == request.PeriodFrom
                && r.PeriodTo == request.PeriodTo, ct);

        if (existing != null)
        {
            foreach (var e in existing.Entries.ToList())
                _unitOfWork.Repository<SalesSummaryEntry>().Remove(e);
            _unitOfWork.Repository<SalesSummaryReport>().Remove(existing);
            await _unitOfWork.SaveChangesAsync(ct);
        }

        var report = new SalesSummaryReport
        {
            RegionId = request.RegionId,
            RegionName = region.Name,
            PeriodFrom = request.PeriodFrom,
            PeriodTo = request.PeriodTo,
            OriginalFileName = request.OriginalFileName,
            UploadedAt = DateTime.UtcNow,
            UploadedBy = uploadedBy,
        };

        await _unitOfWork.Repository<SalesSummaryReport>().AddAsync(report, ct);
        await _unitOfWork.SaveChangesAsync(ct);

        int order = 0;
        var savedEntries = new List<SalesSummaryEntry>();
        foreach (var req in request.Entries)
        {
            var entry = new SalesSummaryEntry
            {
                SalesSummaryReportId = report.Id,
                GroupName = req.GroupName,
                SalesWithTax = req.SalesWithTax,
                Tax = req.Tax,
                NetSales = req.NetSales,
                Discount = req.Discount,
                GrossSales = req.GrossSales,
                IsTotal = req.IsTotal,
                SortOrder = req.SortOrder >= 0 ? req.SortOrder : order,
            };
            await _unitOfWork.Repository<SalesSummaryEntry>().AddAsync(entry, ct);
            savedEntries.Add(entry);
            order++;
        }
        await _unitOfWork.SaveChangesAsync(ct);

        report.Entries = savedEntries;
        return MapToDto(report);
    }

    // ── Admin queries ─────────────────────────────────────────────────────────

    public async Task<List<SalesSummaryReportDto>> GetAllReportsAsync(CancellationToken ct = default)
    {
        var reports = await _unitOfWork.Repository<SalesSummaryReport>().Query()
            .Include(r => r.Entries)
            .OrderByDescending(r => r.UploadedAt)
            .ToListAsync(ct);

        return reports.Select(MapToDto).ToList();
    }

    public async Task<SalesSummaryReportDetailDto> GetReportByIdAsync(Guid reportId, CancellationToken ct = default)
    {
        var report = await _unitOfWork.Repository<SalesSummaryReport>().Query()
            .Include(r => r.Entries.OrderBy(e => e.SortOrder))
            .FirstOrDefaultAsync(r => r.Id == reportId, ct)
            ?? throw new NotFoundException("Sales summary report", reportId);

        return MapToDetailDto(report);
    }

    public async Task DeleteReportAsync(Guid reportId, CancellationToken ct = default)
    {
        var report = await _unitOfWork.Repository<SalesSummaryReport>().Query()
            .Include(r => r.Entries)
            .FirstOrDefaultAsync(r => r.Id == reportId, ct)
            ?? throw new NotFoundException("Sales summary report", reportId);

        foreach (var e in report.Entries.ToList())
            _unitOfWork.Repository<SalesSummaryEntry>().Remove(e);
        _unitOfWork.Repository<SalesSummaryReport>().Remove(report);
        await _unitOfWork.SaveChangesAsync(ct);
    }

    // ── Rep queries (region-restricted) ──────────────────────────────────────

    public async Task<List<SalesSummaryReportDetailDto>> GetRepReportsAsync(Guid repUserId, CancellationToken ct = default)
    {
        var regionIds = await GetAssignedRegionIdsAsync(repUserId, ct);

        var reports = await _unitOfWork.Repository<SalesSummaryReport>().Query()
            .Include(r => r.Entries.OrderBy(e => e.SortOrder))
            .Where(r => regionIds.Contains(r.RegionId))
            .OrderByDescending(r => r.UploadedAt)
            .ToListAsync(ct);

        return reports.Select(MapToDetailDto).ToList();
    }

    public async Task<SalesSummaryReportDetailDto> GetRepReportByIdAsync(Guid reportId, Guid repUserId, CancellationToken ct = default)
    {
        var regionIds = await GetAssignedRegionIdsAsync(repUserId, ct);

        var report = await _unitOfWork.Repository<SalesSummaryReport>().Query()
            .Include(r => r.Entries.OrderBy(e => e.SortOrder))
            .FirstOrDefaultAsync(r => r.Id == reportId && regionIds.Contains(r.RegionId), ct)
            ?? throw new NotFoundException("Sales summary report", reportId);

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

    // ── Mappers ───────────────────────────────────────────────────────────────

    private static SalesSummaryReportDto MapToDto(SalesSummaryReport r)
    {
        var dataRows = r.Entries?.Where(e => !e.IsTotal).ToList() ?? [];
        var totalRow = r.Entries?.FirstOrDefault(e => e.IsTotal);

        return new SalesSummaryReportDto
        {
            Id = r.Id,
            RegionId = r.RegionId,
            RegionName = r.RegionName,
            PeriodFrom = r.PeriodFrom,
            PeriodTo = r.PeriodTo,
            OriginalFileName = r.OriginalFileName,
            UploadedAt = r.UploadedAt,
            UploadedBy = r.UploadedBy,
            RowCount = dataRows.Count,
            TotalSalesWithTax = totalRow?.SalesWithTax ?? dataRows.Sum(e => e.SalesWithTax),
            TotalTax = totalRow?.Tax ?? dataRows.Sum(e => e.Tax),
            TotalNetSales = totalRow?.NetSales ?? dataRows.Sum(e => e.NetSales),
            TotalDiscount = totalRow?.Discount ?? dataRows.Sum(e => e.Discount),
            TotalGrossSales = totalRow?.GrossSales ?? dataRows.Sum(e => e.GrossSales),
        };
    }

    private static SalesSummaryReportDetailDto MapToDetailDto(SalesSummaryReport r) => new()
    {
        Id = r.Id,
        RegionId = r.RegionId,
        RegionName = r.RegionName,
        PeriodFrom = r.PeriodFrom,
        PeriodTo = r.PeriodTo,
        OriginalFileName = r.OriginalFileName,
        UploadedAt = r.UploadedAt,
        UploadedBy = r.UploadedBy,
        Entries = r.Entries
            .OrderBy(e => e.SortOrder)
            .Select(e => new SalesSummaryEntryDto
            {
                Id = e.Id,
                GroupName = e.GroupName,
                SalesWithTax = e.SalesWithTax,
                Tax = e.Tax,
                NetSales = e.NetSales,
                Discount = e.Discount,
                GrossSales = e.GrossSales,
                IsTotal = e.IsTotal,
                SortOrder = e.SortOrder,
            })
            .ToList(),
    };
}
