using DistributionSystem.Domain.Interfaces;

namespace DistributionSystem.Domain.Entities;

public class SalesTarget : IAuditableEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RepId { get; set; }
    public string? TargetName { get; set; }
    public string TargetPeriod { get; set; } = "Monthly"; // Monthly, Quarterly, Yearly
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public decimal TargetAmount { get; set; }
    public decimal AchievedAmount { get; set; }
    public string Status { get; set; } = "Active";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public string? UpdatedBy { get; set; }

    // Navigation
    public SalesRepProfile Rep { get; set; } = null!;
}