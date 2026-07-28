using DistributionSystem.Domain.Interfaces;

namespace DistributionSystem.Domain.Entities;

public class CoordinatorProfile : IAuditableEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string EmployeeCode { get; set; } = string.Empty;
    public Guid? RegionId { get; set; }
    public DateTime HireDate { get; set; }
    public bool IsDeleted { get; set; } = false;
    public DateTime? DeletedAt { get; set; }
    public string? DeletedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public string? UpdatedBy { get; set; }

    // Navigation
    public User User { get; set; } = null!;
    public Region? Region { get; set; }
    public ICollection<RepCoordinator> RepCoordinators { get; set; } = new List<RepCoordinator>();
    public ICollection<CustomerProfile> AssignedCustomers { get; set; } = new List<CustomerProfile>();
}
