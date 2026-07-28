using DistributionSystem.Domain.Interfaces;

namespace DistributionSystem.Domain.Entities;

public class Product : IAuditableEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();

    // core fields coming from import sheet
    public string Name { get; set; } = string.Empty;            // Description/Name
    public string SKU { get; set; } = string.Empty;             // Item/SKU
    public string? Barcode { get; set; }
    public Guid? CategoryId { get; set; }
    public string? Brand { get; set; }
    public decimal SellingPrice { get; set; }                   // Rate from sheet
    public decimal? MRP { get; set; }                          // MRP column from sheet
    public int Quantity { get; set; }                           // Qty from sheet

    // discount / tax metadata
    public decimal? DiscountPercent { get; set; }
    public decimal? DiscountAmount { get; set; }
    public string? TaxCode { get; set; }
    public decimal? TaxAmount { get; set; }
    public decimal? TotalAmount { get; set; }
    public string? UOM { get; set; }

    /// <summary>JSON array of product image URLs e.g. ["/uploads/products/a.jpg",...]</summary>
    public string? ImageUrlsJson { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public string? UpdatedBy { get; set; }

    // Navigation
    public Category? Category { get; set; }
    public ICollection<OrderItem> OrderItems { get; set; } = new List<OrderItem>();
}
