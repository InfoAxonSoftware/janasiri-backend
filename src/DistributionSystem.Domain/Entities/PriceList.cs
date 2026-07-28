namespace DistributionSystem.Domain.Entities;

public class PriceList
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string? CustomerSegment { get; set; }
    public Guid ProductId { get; set; }

    // Either a special price (absolute) or a discount percent can be stored.
    public decimal? SpecialPrice { get; set; }
    public decimal? DiscountPercent { get; set; }

    public DateTime StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public bool IsActive { get; set; } = true;

    // Navigation
    public Product Product { get; set; } = null!;
}
