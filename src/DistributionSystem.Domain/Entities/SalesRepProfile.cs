using DistributionSystem.Domain.Interfaces;

namespace DistributionSystem.Domain.Entities;

public class SalesRepProfile : IAuditableEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string EmployeeCode { get; set; } = string.Empty;
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
    public ICollection<RepRegion> Regions { get; set; } = new List<RepRegion>();
    public ICollection<RepSubRegion> SubRegions { get; set; } = new List<RepSubRegion>();
    public ICollection<RepCoordinator> Coordinators { get; set; } = new List<RepCoordinator>();
    public ICollection<RepRoute> AssignedRoutes { get; set; } = new List<RepRoute>();
    public ICollection<Order> Orders { get; set; } = new List<Order>();
    public ICollection<Visit> Visits { get; set; } = new List<Visit>();
    public ICollection<Payment> CollectedPayments { get; set; } = new List<Payment>();
    public ICollection<SalesTarget> SalesTargets { get; set; } = new List<SalesTarget>();
    public ICollection<CustomerProfile> AssignedCustomers { get; set; } = new List<CustomerProfile>();
}
