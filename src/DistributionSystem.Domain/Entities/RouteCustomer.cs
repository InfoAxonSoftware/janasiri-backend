namespace DistributionSystem.Domain.Entities;

public class RouteCustomer
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RouteId { get; set; }
    public Guid CustomerId { get; set; }
    public int VisitOrder { get; set; }
    public string? VisitFrequency { get; set; } // Weekly, BiWeekly, Monthly

    // Navigation
    public Route Route { get; set; } = null!;
    public CustomerProfile Customer { get; set; } = null!;
}
