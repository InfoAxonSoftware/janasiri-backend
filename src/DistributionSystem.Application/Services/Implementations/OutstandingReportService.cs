using DistributionSystem.Application.DTOs.Outstanding;
using DistributionSystem.Application.Exceptions;
using DistributionSystem.Application.Services.Interfaces;
using DistributionSystem.Domain.Entities;
using DistributionSystem.Infrastructure.Data.Repositories.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using MiniExcelLibs;

namespace DistributionSystem.Application.Services.Implementations;

public class OutstandingReportService : IOutstandingReportService
{
    private readonly IUnitOfWork _unitOfWork;

    public OutstandingReportService(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    // ── Upload ────────────────────────────────────────────────────────────────

    public async Task<OutstandingReportDto> UploadReportAsync(
        Guid regionId, DateTime? reportDate, IFormFile file, string uploadedBy, CancellationToken ct = default)
    {
        // Validate region exists
        var region = await _unitOfWork.Repository<Region>().Query()
            .FirstOrDefaultAsync(r => r.Id == regionId, ct)
            ?? throw new NotFoundException("Region", regionId);

        // Delete existing report for this region if any
        var existing = await _unitOfWork.Repository<OutstandingReport>().Query()
            .Include(r => r.Entries)
            .FirstOrDefaultAsync(r => r.RegionId == regionId, ct);

        if (existing != null)
        {
            foreach (var e in existing.Entries.ToList())
                _unitOfWork.Repository<OutstandingEntry>().Remove(e);
            _unitOfWork.Repository<OutstandingReport>().Remove(existing);
            await _unitOfWork.SaveChangesAsync(ct);
        }

        // Parse Excel
        using var stream = file.OpenReadStream();
        var entries = ParseExcel(stream);

        var report = new OutstandingReport
        {
            RegionId = regionId,
            RegionName = region.Name,
            ReportDate = reportDate ?? DateTime.UtcNow.Date,
            UploadedAt = DateTime.UtcNow,
            UploadedBy = uploadedBy,
        };

        await _unitOfWork.Repository<OutstandingReport>().AddAsync(report, ct);
        await _unitOfWork.SaveChangesAsync(ct);

        int order = 0;
        foreach (var e in entries)
        {
            e.OutstandingReportId = report.Id;
            e.SortOrder = order++;
            await _unitOfWork.Repository<OutstandingEntry>().AddAsync(e, ct);
        }
        await _unitOfWork.SaveChangesAsync(ct);

        return MapToDto(report, entries.Count);
    }

    // ── Chunk-upload ──────────────────────────────────────────────────────────

    public async Task<StartOutstandingUploadResponse> StartUploadAsync(
        Guid regionId, DateTime? reportDate, string uploadedBy, CancellationToken ct = default)
    {
        // Validate region
        var region = await _unitOfWork.Repository<Region>().Query()
            .FirstOrDefaultAsync(r => r.Id == regionId, ct)
            ?? throw new NotFoundException("Region", regionId);

        // Remove existing report
        var existing = await _unitOfWork.Repository<OutstandingReport>().Query()
            .Include(r => r.Entries)
            .FirstOrDefaultAsync(r => r.RegionId == regionId, ct);

        if (existing != null)
        {
            foreach (var e in existing.Entries.ToList())
                _unitOfWork.Repository<OutstandingEntry>().Remove(e);
            _unitOfWork.Repository<OutstandingReport>().Remove(existing);
            await _unitOfWork.SaveChangesAsync(ct);
        }

        var report = new OutstandingReport
        {
            RegionId = regionId,
            RegionName = region.Name,
            ReportDate = reportDate ?? DateTime.UtcNow.Date,
            UploadedAt = DateTime.UtcNow,
            UploadedBy = uploadedBy,
        };

        await _unitOfWork.Repository<OutstandingReport>().AddAsync(report, ct);
        await _unitOfWork.SaveChangesAsync(ct);

        return new StartOutstandingUploadResponse { ReportId = report.Id };
    }

    public async Task AppendEntriesAsync(
        Guid reportId, List<OutstandingEntryRequest> entries, CancellationToken ct = default)
    {
        // Verify report exists
        var report = await _unitOfWork.Repository<OutstandingReport>().Query()
            .FirstOrDefaultAsync(r => r.Id == reportId, ct)
            ?? throw new NotFoundException("Outstanding report", reportId);

        // Determine current max sort order to continue sequence
        var maxOrder = await _unitOfWork.Repository<OutstandingEntry>().Query()
            .Where(e => e.OutstandingReportId == reportId)
            .Select(e => (int?)e.SortOrder)
            .MaxAsync(ct) ?? -1;

        int order = maxOrder + 1;
        foreach (var req in entries)
        {
            var entry = new OutstandingEntry
            {
                OutstandingReportId = reportId,
                CustomerName = req.CustomerName,
                TxnType = req.TxnType,
                RefNo = req.RefNo,
                TxnDate = req.TxnDate,
                AgeDays = req.AgeDays,
                Current = req.Current,
                Bucket1_15 = req.Bucket1_15,
                Bucket16_30 = req.Bucket16_30,
                Bucket31_45 = req.Bucket31_45,
                Above45 = req.Above45,
                Balance = req.Balance,
                IsTotal = req.IsTotal,
                SortOrder = req.SortOrder >= 0 ? req.SortOrder : order,
            };
            await _unitOfWork.Repository<OutstandingEntry>().AddAsync(entry, ct);
            order++;
        }
        await _unitOfWork.SaveChangesAsync(ct);
    }

    // ── Admin queries ─────────────────────────────────────────────────────────

    public async Task<List<OutstandingReportDto>> GetAllReportsAsync(CancellationToken ct = default)
    {
        var reports = await _unitOfWork.Repository<OutstandingReport>().Query()
            .Include(r => r.Entries)
            .OrderBy(r => r.RegionName)
            .ToListAsync(ct);

        return reports.Select(r => MapToDto(r, r.Entries.Count)).ToList();
    }

    public async Task<OutstandingReportDetailDto> GetReportByRegionAsync(Guid regionId, CancellationToken ct = default)
    {
        var report = await _unitOfWork.Repository<OutstandingReport>().Query()
            .Include(r => r.Entries.OrderBy(e => e.SortOrder))
            .FirstOrDefaultAsync(r => r.RegionId == regionId, ct)
            ?? throw new NotFoundException("Outstanding report for region", regionId);

        return MapToDetailDto(report);
    }

    public async Task DeleteReportAsync(Guid regionId, CancellationToken ct = default)
    {
        var report = await _unitOfWork.Repository<OutstandingReport>().Query()
            .Include(r => r.Entries)
            .FirstOrDefaultAsync(r => r.RegionId == regionId, ct)
            ?? throw new NotFoundException("Outstanding report for region", regionId);

        foreach (var e in report.Entries.ToList())
            _unitOfWork.Repository<OutstandingEntry>().Remove(e);
        _unitOfWork.Repository<OutstandingReport>().Remove(report);
        await _unitOfWork.SaveChangesAsync(ct);
    }

    // ── Rep query ─────────────────────────────────────────────────────────────

    public async Task<List<OutstandingReportDetailDto>> GetRepReportsAsync(Guid repUserId, CancellationToken ct = default)
    {
        // Resolve rep profile
        var rep = await _unitOfWork.Repository<SalesRepProfile>().Query()
            .Include(r => r.Regions)
            .FirstOrDefaultAsync(r => r.UserId == repUserId, ct)
            ?? throw new NotFoundException("Sales rep profile", repUserId);

        var regionIds = rep.Regions.Select(rr => rr.RegionId).ToList();

        var reports = await _unitOfWork.Repository<OutstandingReport>().Query()
            .Include(r => r.Entries.OrderBy(e => e.SortOrder))
            .Where(r => regionIds.Contains(r.RegionId))
            .OrderBy(r => r.RegionName)
            .ToListAsync(ct);

        return reports.Select(MapToDetailDto).ToList();
    }

    // ── Excel parsing ─────────────────────────────────────────────────────────

    /// <summary>
    /// Parses the outstanding ageing Excel file.
    ///
    /// Column lookup is fully case-insensitive and trims whitespace from headers.
    /// MiniExcel may return numeric cells as double/int and date cells as DateTime — all handled.
    ///
    /// Rows where TxnType AND RefNo are both blank (but have numeric values) are treated as customer total rows.
    /// </summary>
    private static List<OutstandingEntry> ParseExcel(Stream stream)
    {
        var rows = stream.Query(useHeaderRow: true).Cast<IDictionary<string, object?>>().ToList();
        var entries = new List<OutstandingEntry>();
        string currentCustomer = string.Empty;

        foreach (IDictionary<string, object?> rawRow in rows)
        {
            // Build a case-insensitive, trimmed-key dictionary for robust column lookup
            var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in rawRow)
                if (kv.Key != null)
                    row[kv.Key.Trim()] = kv.Value;

            // ── helpers ──────────────────────────────────────────────────────
            string Get(params string[] keys)
            {
                foreach (var k in keys)
                {
                    if (!row.TryGetValue(k.Trim(), out var v) || v == null) continue;
                    var s = v.ToString()!.Trim();
                    if (!string.IsNullOrEmpty(s)) return s;
                }
                return string.Empty;
            }

            decimal GetDec(params string[] keys)
            {
                foreach (var k in keys)
                {
                    if (!row.TryGetValue(k.Trim(), out var v) || v == null) continue;
                    if (v is decimal dec) return dec;
                    if (v is double dbl)  return (decimal)dbl;
                    if (v is int  ii)     return ii;
                    if (v is long ll)     return ll;
                    // Remove thousands separator commas before parsing
                    var s = v.ToString()!.Replace(",", "").Trim();
                    if (decimal.TryParse(s,
                            System.Globalization.NumberStyles.Any,
                            System.Globalization.CultureInfo.InvariantCulture, out var d))
                        return d;
                }
                return 0m;
            }

            DateTime? GetDate(params string[] keys)
            {
                foreach (var k in keys)
                {
                    if (!row.TryGetValue(k.Trim(), out var v) || v == null) continue;
                    if (v is DateTime dt) return dt;
                    var s = v.ToString()!.Trim();
                    if (string.IsNullOrEmpty(s)) continue;
                    // Try standard parse first, then explicit formats
                    if (DateTime.TryParse(s,
                            System.Globalization.CultureInfo.InvariantCulture,
                            System.Globalization.DateTimeStyles.None, out var d1))
                        return d1;
                    if (DateTime.TryParseExact(s,
                            ["dd/MM/yyyy", "d/M/yyyy", "MM/dd/yyyy", "yyyy-MM-dd", "dd-MM-yyyy"],
                            System.Globalization.CultureInfo.InvariantCulture,
                            System.Globalization.DateTimeStyles.None, out var d2))
                        return d2;
                }
                return null;
            }

            int? GetInt(params string[] keys)
            {
                foreach (var k in keys)
                {
                    if (!row.TryGetValue(k.Trim(), out var v) || v == null) continue;
                    if (v is int  ii) return ii;
                    if (v is long ll) return (int)ll;
                    if (v is double dbl) return (int)dbl;
                    if (int.TryParse(v.ToString()!.Trim(), out var parsed)) return parsed;
                }
                return null;
            }

            // ── column extraction (ordered from most-specific to least) ─────
            var txnType  = Get("Txn Type", "TxnType", "Trans Type", "Transaction Type", "Type", "Txn");
            var customer = Get("Customer", "Customer Name", "CUSTOMER", "Debtor", "Client");
            var refNo    = Get("Ref No", "RefNo", "Ref. No.", "Ref No.", "Reference No", "Doc No",
                               "Invoice No", "Invoice #", "Doc #", "Trans. No", "No.", "Reference");
            var txnDate  = GetDate("Date", "Txn Date", "Trans. Date", "Transaction Date",
                                   "Invoice Date", "Doc Date");
            var ageDays  = GetInt("Age Days", "AgeDays", "Age", "Age (Days)", "Due Days",
                                  "Overdue Days", "Days");

            var current  = GetDec("Current", "Not Yet Due", "Not Due");
            var b1_15    = GetDec("1-15", "1 - 15", "1 to 15", "1 To 15", "01-15", "1-15 Days");
            var b16_30   = GetDec("16-30", "16 - 30", "16 to 30", "16 To 30", "16-30 Days");
            var b31_45   = GetDec("31-45", "31 - 45", "31 to 45", "31 To 45", "31-45 Days");
            var above45  = GetDec("Above 45 (F)", "Above 45", "Above45", "Over 45",
                                  "46+", "> 45", "45+", "Above 45 Days", "46 & Above");
            var balance  = GetDec("Balance", "Outstanding", "O/S Balance", "OS Balance",
                                  "Amount Due", "Total");

            // ── skip fully blank rows ────────────────────────────────────────
            bool allBlank = string.IsNullOrEmpty(txnType) && string.IsNullOrEmpty(customer)
                && string.IsNullOrEmpty(refNo)
                && current == 0 && b1_15 == 0 && b16_30 == 0
                && b31_45 == 0 && above45 == 0 && balance == 0;
            if (allBlank) continue;

            bool isTotal = string.IsNullOrEmpty(txnType) && string.IsNullOrEmpty(refNo);

            if (!string.IsNullOrEmpty(customer))
                currentCustomer = customer;

            entries.Add(new OutstandingEntry
            {
                CustomerName = isTotal
                    ? currentCustomer
                    : (string.IsNullOrEmpty(customer) ? currentCustomer : customer),
                TxnType     = string.IsNullOrEmpty(txnType) ? null : txnType,
                RefNo       = string.IsNullOrEmpty(refNo)   ? null : refNo,
                TxnDate     = txnDate,
                AgeDays     = ageDays,
                Current     = current,
                Bucket1_15  = b1_15,
                Bucket16_30 = b16_30,
                Bucket31_45 = b31_45,
                Above45     = above45,
                Balance     = balance,
                IsTotal     = isTotal,
            });
        }

        return entries;
    }

    // ── Mappers ───────────────────────────────────────────────────────────────

    private static OutstandingReportDto MapToDto(OutstandingReport r, int entryCount)
    {
        var nonTotal = r.Entries?.Where(e => !e.IsTotal).ToList() ?? [];
        return new OutstandingReportDto
        {
            Id = r.Id,
            RegionId = r.RegionId,
            RegionName = r.RegionName,
            ReportDate = r.ReportDate,
            UploadedAt = r.UploadedAt,
            UploadedBy = r.UploadedBy,
            CustomerCount = r.Entries != null
                ? r.Entries.Select(e => e.CustomerName).Distinct().Count()
                : 0,
            EntryCount = entryCount,
        };
    }

    private static OutstandingReportDetailDto MapToDetailDto(OutstandingReport r)
    {
        return new OutstandingReportDetailDto
        {
            Id = r.Id,
            RegionId = r.RegionId,
            RegionName = r.RegionName,
            ReportDate = r.ReportDate,
            UploadedAt = r.UploadedAt,
            UploadedBy = r.UploadedBy,
            Entries = r.Entries
                .OrderBy(e => e.SortOrder)
                .Select(e => new OutstandingEntryDto
                {
                    Id = e.Id,
                    CustomerName = e.CustomerName,
                    TxnType = e.TxnType,
                    RefNo = e.RefNo,
                    TxnDate = e.TxnDate,
                    AgeDays = e.AgeDays,
                    Current = e.Current,
                    Bucket1_15 = e.Bucket1_15,
                    Bucket16_30 = e.Bucket16_30,
                    Bucket31_45 = e.Bucket31_45,
                    Above45 = e.Above45,
                    Balance = e.Balance,
                    IsTotal = e.IsTotal,
                    SortOrder = e.SortOrder,
                })
                .ToList(),
        };
    }
}
