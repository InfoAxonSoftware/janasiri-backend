namespace DistributionSystem.Application.DTOs.TargetReports;

// ── Response DTOs ────────────────────────────────────────────────────────────

public class TargetReportSummaryDto
{
    public Guid Id { get; set; }
    public Guid TargetId { get; set; }
    public string? OriginalFileName { get; set; }
    public DateTime FromDate { get; set; }
    public DateTime AsAtDate { get; set; }
    public decimal ActualSales { get; set; }
    public int DistinctOrderCount { get; set; }
    public int DistinctCustomerCount { get; set; }
    public bool IsCurrent { get; set; }
    public DateTime UploadedAt { get; set; }
    public string? UploadedBy { get; set; }
}

public class TargetReportSourceRowDto
{
    public string RowType { get; set; } = string.Empty;
    public string? TxnType { get; set; }
    public DateTime? TxnDate { get; set; }
    public string? RefNo { get; set; }
    public string? CustomerName { get; set; }
    public string? ItemDescription { get; set; }
    public decimal? Qty { get; set; }
    public decimal? Discount { get; set; }
    public decimal? SalesWithTax { get; set; }
    public int SortOrder { get; set; }
}

public class TargetReportEntryDto
{
    public Guid Id { get; set; }
    public DateTime TxnDate { get; set; }
    public string? RefNo { get; set; }
    public string? CustomerName { get; set; }
    public string? ItemDescription { get; set; }
    public decimal Qty { get; set; }
    public decimal Discount { get; set; }
    public decimal SalesWithTax { get; set; }
    public int SortOrder { get; set; }
}

public class TargetReportDetailDto
{
    public Guid Id { get; set; }
    public Guid TargetId { get; set; }
    public Guid RepId { get; set; }
    public string RepName { get; set; } = string.Empty;
    public string TargetPeriod { get; set; } = string.Empty;
    public DateTime TargetStartDate { get; set; }
    public DateTime TargetEndDate { get; set; }
    public decimal TargetAmount { get; set; }
    public string? OriginalFileName { get; set; }
    public DateTime FromDate { get; set; }
    public DateTime AsAtDate { get; set; }
    public decimal ActualSales { get; set; }
    public int DistinctOrderCount { get; set; }
    public int DistinctCustomerCount { get; set; }
    public bool IsCurrent { get; set; }
    public DateTime UploadedAt { get; set; }
    public string? UploadedBy { get; set; }
    public List<TargetReportEntryDto> Entries { get; set; } = [];
    public List<TargetReportSourceRowDto> SourceRows { get; set; } = [];
}

// ── Request DTOs ─────────────────────────────────────────────────────────────

/// <summary>One client-parsed "Invoice" row from the detailed sales report Excel.</summary>
public class TargetReportEntryRequest
{
    public DateTime TxnDate { get; set; }
    public string? RefNo { get; set; }
    public string? CustomerName { get; set; }
    public string? ItemDescription { get; set; }
    public decimal Qty { get; set; }
    public decimal Discount { get; set; }
    public decimal SalesWithTax { get; set; }
    public int SortOrder { get; set; }
}

public class UploadTargetReportRequest
{
    public string? OriginalFileName { get; set; }
    public List<TargetReportEntryRequest> Entries { get; set; } = [];
    public List<TargetReportSourceRowDto> SourceRows { get; set; } = [];
    /// <summary>Must be true to replace a current report with one whose As-At date is older.</summary>
    public bool Force { get; set; }
}
