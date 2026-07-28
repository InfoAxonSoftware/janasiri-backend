using DistributionSystem.Domain.Interfaces;

namespace DistributionSystem.Domain.Entities;

public class RepSubRegion : IAuditableEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RepId { get; set; }
    public Guid SubRegionId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public string? UpdatedBy { get; set; }

    public SalesRepProfile Rep { get; set; } = null!;
    public SubRegion SubRegion { get; set; } = null!;
}
