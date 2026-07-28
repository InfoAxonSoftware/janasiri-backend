namespace DistributionSystem.Domain.Entities;

/// <summary>One parsed "Invoice" row from a detailed sales report Excel upload.</summary>
public class TargetSalesReportEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TargetSalesReportId { get; set; }
    public DateTime TxnDate { get; set; }
    public string? RefNo { get; set; }
    public string? CustomerName { get; set; }
    public string? ItemDescription { get; set; }
    public decimal Qty { get; set; }
    public decimal Discount { get; set; }
    public decimal SalesWithTax { get; set; }
    public int SortOrder { get; set; }

    public TargetSalesReport Report { get; set; } = null!;
}
