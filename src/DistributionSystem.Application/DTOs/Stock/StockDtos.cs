namespace DistributionSystem.Application.DTOs.Stock;

// ── Response DTOs ────────────────────────────────────────────────────────────

public class StockReportSummaryDto
{
    public Guid Id { get; set; }
    public Guid RegionId { get; set; }
    public string RegionName { get; set; } = string.Empty;
    public string CompanyName { get; set; } = string.Empty;
    public string ReportTitle { get; set; } = string.Empty;
    public DateOnly? DateAsOf { get; set; }
    public DateTimeOffset? ExportedAt { get; set; }
    public int RowCount { get; set; }
    public decimal TotalOnHand { get; set; }
    public decimal TotalAmount { get; set; }
    public DateTime UploadedAt { get; set; }
    public string? UploadedBy { get; set; }
}

public class StockReportRowDto
{
    public Guid Id { get; set; }
    public string RowType { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public string? GroupName { get; set; }
    public string? Item { get; set; }
    public string? SalesDescription { get; set; }
    public decimal? CostExVat { get; set; }
    public decimal? OnHand { get; set; }
    public decimal? Amount { get; set; }
}

public class StockReportDetailDto : StockReportSummaryDto
{
    public List<StockReportRowDto> Rows { get; set; } = [];
}
