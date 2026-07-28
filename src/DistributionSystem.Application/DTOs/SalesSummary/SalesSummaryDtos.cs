namespace DistributionSystem.Application.DTOs.SalesSummary;

// ── Response DTOs ────────────────────────────────────────────────────────────

public class SalesSummaryReportDto
{
    public Guid Id { get; set; }
    public Guid RegionId { get; set; }
    public string RegionName { get; set; } = string.Empty;
    public DateTime? PeriodFrom { get; set; }
    public DateTime? PeriodTo { get; set; }
    public string? OriginalFileName { get; set; }
    public DateTime UploadedAt { get; set; }
    public string? UploadedBy { get; set; }
    public int RowCount { get; set; }
    public decimal TotalSalesWithTax { get; set; }
    public decimal TotalTax { get; set; }
    public decimal TotalNetSales { get; set; }
    public decimal TotalDiscount { get; set; }
    public decimal TotalGrossSales { get; set; }
}

public class SalesSummaryEntryDto
{
    public Guid Id { get; set; }
    public string GroupName { get; set; } = string.Empty;
    public decimal SalesWithTax { get; set; }
    public decimal Tax { get; set; }
    public decimal NetSales { get; set; }
    public decimal Discount { get; set; }
    public decimal GrossSales { get; set; }
    public bool IsTotal { get; set; }
    public int SortOrder { get; set; }
}

public class SalesSummaryReportDetailDto
{
    public Guid Id { get; set; }
    public Guid RegionId { get; set; }
    public string RegionName { get; set; } = string.Empty;
    public DateTime? PeriodFrom { get; set; }
    public DateTime? PeriodTo { get; set; }
    public string? OriginalFileName { get; set; }
    public DateTime UploadedAt { get; set; }
    public string? UploadedBy { get; set; }
    public List<SalesSummaryEntryDto> Entries { get; set; } = [];
}

// ── Request DTOs ─────────────────────────────────────────────────────────────

/// <summary>One row sent from the client after client-side Excel parsing.</summary>
public class SalesSummaryEntryRequest
{
    public string GroupName { get; set; } = string.Empty;
    public decimal SalesWithTax { get; set; }
    public decimal Tax { get; set; }
    public decimal NetSales { get; set; }
    public decimal Discount { get; set; }
    public decimal GrossSales { get; set; }
    public bool IsTotal { get; set; }
    public int SortOrder { get; set; }
}

/// <summary>Uploads a fully client-parsed Sales Summary report in a single call.</summary>
public class UploadSalesSummaryRequest
{
    public Guid RegionId { get; set; }
    public DateTime? PeriodFrom { get; set; }
    public DateTime? PeriodTo { get; set; }
    public string? OriginalFileName { get; set; }
    public List<SalesSummaryEntryRequest> Entries { get; set; } = [];
}
