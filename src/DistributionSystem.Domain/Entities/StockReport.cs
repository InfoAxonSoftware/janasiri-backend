namespace DistributionSystem.Domain.Entities;

public enum StockReportRowType
{
    GroupHeader,
    Item,
    GroupSubtotal,
    GrandTotal,
    Blank
}

/// <summary>Holds one uploaded stock summary report per region.</summary>
public class StockReport
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>FK to Region</summary>
    public Guid RegionId { get; set; }

    /// <summary>Snapshot of region name at upload time</summary>
    public string RegionName { get; set; } = string.Empty;

    /// <summary>e.g. "JANASIRI DISTRIBUTORS (PVT) LTD"</summary>
    public string CompanyName { get; set; } = string.Empty;

    /// <summary>e.g. "STOCK SUMMARY - DELMEGE FORSYTH & CO LTD / LIMIT 2,500,000.00"</summary>
    public string ReportTitle { get; set; } = string.Empty;

    /// <summary>"DATE AS OF" from the report header</summary>
    public DateOnly? DateAsOf { get; set; }

    /// <summary>"Export Date and Time" from the report header, parsed with the +05:30 (Sri Lanka) offset</summary>
    public DateTimeOffset? ExportedAt { get; set; }

    public string? OriginalFileName { get; set; }

    /// <summary>Count of Item rows only (excludes GroupHeader/GroupSubtotal/GrandTotal/Blank)</summary>
    public int RowCount { get; set; }

    /// <summary>Taken verbatim from the Grand Total row — never recomputed</summary>
    public decimal TotalOnHand { get; set; }

    /// <summary>Taken verbatim from the Grand Total row — never recomputed</summary>
    public decimal TotalAmount { get; set; }

    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;
    public string? UploadedBy { get; set; }

    public Region Region { get; set; } = null!;
    public ICollection<StockReportRow> Rows { get; set; } = new List<StockReportRow>();
}
