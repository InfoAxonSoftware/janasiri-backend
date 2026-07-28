using DistributionSystem.Domain.Interfaces;

namespace DistributionSystem.Domain.Entities;

public class QuotationItem : IAuditableEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid QuotationId { get; set; }
    public Guid? ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string? ProductSKU { get; set; }
    public decimal? MRP { get; set; }
    public string? TaxCode { get; set; }
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal? ExpectedPrice { get; set; }
    public decimal DiscountPercent { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal LineTotal { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public string? UpdatedBy { get; set; }

    // Navigation
    public Quotation Quotation { get; set; } = null!;
    public Product? Product { get; set; }
}
