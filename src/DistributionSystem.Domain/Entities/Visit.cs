using DistributionSystem.Domain.Enums;
using DistributionSystem.Domain.Interfaces;
using DistributionSystem.Domain.ValueObjects;

namespace DistributionSystem.Domain.Entities;

public class Visit : IAuditableEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RepId { get; set; }
    public Guid CustomerId { get; set; }
    public Guid? RouteId { get; set; }
    public DateTime PlannedDate { get; set; }
    public DateTime? ActualStartTime { get; set; }
    public DateTime? ActualEndTime { get; set; }
    public Location? CheckInLocation { get; set; }
    public Location? CheckOutLocation { get; set; }
    public VisitStatus Status { get; set; } = VisitStatus.Planned;
    public string? Notes { get; set; }
    public string? OutcomeReason { get; set; }
    public int OrdersPlaced { get; set; }
    public decimal PaymentsCollected { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public string? UpdatedBy { get; set; }

    // Navigation
    public SalesRepProfile Rep { get; set; } = null!;
    public CustomerProfile Customer { get; set; } = null!;
    public Route? Route { get; set; }
}
