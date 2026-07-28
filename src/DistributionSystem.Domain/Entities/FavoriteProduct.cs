namespace DistributionSystem.Domain.Entities;

public class FavoriteProduct
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CustomerId { get; set; }
    public Guid ProductId { get; set; }
    public DateTime AddedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public CustomerProfile Customer { get; set; } = null!;
    public Product Product { get; set; } = null!;
}
