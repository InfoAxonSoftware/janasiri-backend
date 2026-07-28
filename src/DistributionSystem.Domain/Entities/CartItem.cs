namespace DistributionSystem.Domain.Entities;

public class CartItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CustomerId { get; set; }
    public Guid ProductId { get; set; }
    public int Quantity { get; set; }
    public DateTime AddedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    // Navigation
    public CustomerProfile Customer { get; set; } = null!;
    public Product Product { get; set; } = null!;
}
