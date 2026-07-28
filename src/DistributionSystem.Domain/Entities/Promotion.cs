using DistributionSystem.Domain.Interfaces;

namespace DistributionSystem.Domain.Entities;

public class Promotion : IAuditableEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string PromotionType { get; set; } = string.Empty; // Percentage, BuyXGetY, FlatDiscount
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public decimal? DiscountPercent { get; set; }
    public int? BuyQuantity { get; set; }
    public int? GetQuantity { get; set; }
    public string? ApplicableProductIds { get; set; } // JSON array
    public string? ApplicableCustomerSegments { get; set; } // JSON array
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public string? UpdatedBy { get; set; }
}
