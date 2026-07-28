namespace DistributionSystem.Domain.Entities;

public class OrderItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrderId { get; set; }
    // product reference is nullable now; when a catalog item is deleted we’ll set
    // the foreign key to null but continue using the name/sku snapshot for display.
    public Guid? ProductId { get; set; }

    // snapshot of the product at time of order – keeps historical records intact when the
    // admin later renames or removes the product.  ProductName is required; SKU is
    // optional because not all products may have one.
    public string ProductName { get; set; } = string.Empty;
    public string? ProductSKU { get; set; }
    public string? TaxCode { get; set; }

    public int Quantity { get; set; }
    public int BackorderedQuantity { get; set; } = 0; // new: quantity that will be backordered
    public decimal UnitPrice { get; set; }
    public decimal DiscountPercent { get; set; }
    public decimal? MRP { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal LineTotal { get; set; }

    // Navigation
    public Order Order { get; set; } = null!;
    public Product Product { get; set; } = null!;
}
