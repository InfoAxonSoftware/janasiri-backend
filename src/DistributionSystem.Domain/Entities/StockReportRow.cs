namespace DistributionSystem.Domain.Entities;

/// <summary>One row from the uploaded stock summary Excel file (GroupHeader / Item / GroupSubtotal / GrandTotal / Blank).</summary>
public class StockReportRow
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid StockReportId { get; set; }
    public StockReportRowType RowType { get; set; }

    /// <summary>Preserves the original Excel row order</summary>
    public int SortOrder { get; set; }

    public string? GroupName { get; set; }
    public string? Item { get; set; }
    public string? SalesDescription { get; set; }
    public decimal? CostExVat { get; set; }
    public decimal? OnHand { get; set; }
    public decimal? Amount { get; set; }

    public StockReport Report { get; set; } = null!;
}
