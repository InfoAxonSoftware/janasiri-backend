namespace DistributionSystem.Domain.Entities;

/// <summary>
/// One uploaded "detailed sales report" Excel snapshot attached to a SalesTarget.
/// Cumulative — each upload's ActualSales replaces the target's current figure.
/// Previous uploads are kept (IsCurrent = false) as an audit/upload history, never deleted.
/// </summary>
public class TargetSalesReport
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TargetId { get; set; }
    public Guid RepId { get; set; }
    public string? OriginalFileName { get; set; }
    public DateTime FromDate { get; set; }
    public DateTime AsAtDate { get; set; }
    public decimal ActualSales { get; set; }
    public int DistinctOrderCount { get; set; }
    public int DistinctCustomerCount { get; set; }
    public bool IsCurrent { get; set; }
    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;
    public Guid UploadedByUserId { get; set; }
    public string? UploadedBy { get; set; }

    /// <summary>
    /// Exact source worksheet rows serialized as JSON. Preserves Invoice rows,
    /// daily subtotal rows, blank separators, Total label and grand-total row.
    /// </summary>
    public string? SourceRowsJson { get; set; }

    public SalesTarget Target { get; set; } = null!;
    public ICollection<TargetSalesReportEntry> Entries { get; set; } = new List<TargetSalesReportEntry>();
}
