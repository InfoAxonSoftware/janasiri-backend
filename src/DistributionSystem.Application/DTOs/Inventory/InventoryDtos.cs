using DistributionSystem.Domain.Enums;

namespace DistributionSystem.Application.DTOs.Inventory;

public class InventoryDto
{
    public Guid Id { get; set; }
    public Guid ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string SKU { get; set; } = string.Empty;
    public int QuantityInStock { get; set; }
    public int ReorderLevel { get; set; }
    public int MaxStockLevel { get; set; }
    public DateTime? LastRestockedAt { get; set; }
    public string Status => QuantityInStock <= 0 ? "Out of Stock" : QuantityInStock <= ReorderLevel ? "Low Stock" : "In Stock";
}

public class StockMovementDto
{
    public Guid Id { get; set; }
    public Guid ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string MovementType { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public string? Reason { get; set; }
    public string? ReferenceNumber { get; set; }
    public DateTime CreatedAt { get; set; }
}
