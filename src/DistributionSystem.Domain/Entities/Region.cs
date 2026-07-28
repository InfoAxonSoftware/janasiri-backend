using DistributionSystem.Domain.Interfaces;

namespace DistributionSystem.Domain.Entities;

public class Region : IAuditableEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public string? UpdatedBy { get; set; }

    // Navigation
    public ICollection<SubRegion> SubRegions { get; set; } = new List<SubRegion>();
    public ICollection<CoordinatorProfile> Coordinators { get; set; } = new List<CoordinatorProfile>();
}
