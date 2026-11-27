namespace DistributionSystem.Domain.Entities;
public sealed class OrderAdditionalRep
{
    public Guid OrderId { get; set; }
    public Order Order { get; set; } = null!;
    public Guid RepId { get; set; }
    public SalesRepProfile Rep { get; set; } = null!;
}