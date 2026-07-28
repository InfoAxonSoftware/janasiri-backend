namespace DistributionSystem.Domain.Entities;

public class PaymentAllocation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PaymentId { get; set; }
    public Guid OrderId { get; set; }
    public decimal AllocatedAmount { get; set; }
    public DateTime AllocatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public Payment Payment { get; set; } = null!;
    public Order Order { get; set; } = null!;
}
