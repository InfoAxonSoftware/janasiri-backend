using DistributionSystem.Domain.Interfaces;

namespace DistributionSystem.Domain.Entities;

public class RepRoute : IAuditableEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RouteId { get; set; }
    public Guid RepId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public string? UpdatedBy { get; set; }

    public Route Route { get; set; } = null!;
    public SalesRepProfile Rep { get; set; } = null!;
}