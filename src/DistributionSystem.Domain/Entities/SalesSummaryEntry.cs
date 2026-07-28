namespace DistributionSystem.Domain.Entities;

public class SalesSummaryEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SalesSummaryReportId { get; set; }
    public string GroupName { get; set; } = string.Empty; // e.g. a date label such as "2026.06.01"
    public decimal SalesWithTax { get; set; }
    public decimal Tax { get; set; }
    public decimal NetSales { get; set; }
    public decimal Discount { get; set; }
    public decimal GrossSales { get; set; }
    public bool IsTotal { get; set; }
    public int SortOrder { get; set; }

    public SalesSummaryReport Report { get; set; } = null!;
}
