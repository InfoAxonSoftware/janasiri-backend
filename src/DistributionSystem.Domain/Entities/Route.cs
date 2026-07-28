using DistributionSystem.Domain.Interfaces;

namespace DistributionSystem.Domain.Entities;

public class Route : IAuditableEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string DaysOfWeek { get; set; } = "[]"; // JSON array
    public int EstimatedDurationMinutes { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public string? UpdatedBy { get; set; }

    // Navigation
    public ICollection<RepRoute> AssignedReps { get; set; } = new List<RepRoute>();
    public ICollection<RouteCustomer> RouteCustomers { get; set; } = new List<RouteCustomer>();
    public ICollection<Visit> Visits { get; set; } = new List<Visit>();
}
