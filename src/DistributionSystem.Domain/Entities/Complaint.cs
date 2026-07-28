using DistributionSystem.Domain.Enums;
using DistributionSystem.Domain.Interfaces;

namespace DistributionSystem.Domain.Entities;

public class Complaint : IAuditableEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? CustomerId { get; set; }
    public Guid? OrderId { get; set; }
    public string Subject { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public ComplaintPriority Priority { get; set; } = ComplaintPriority.Medium;
    public ComplaintStatus Status { get; set; } = ComplaintStatus.Open;
    public Guid? AssignedTo { get; set; }
    public Guid? ResolvedBy { get; set; }
    public DateTime? ResolvedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public string CreatedByRole { get; set; } = string.Empty;
    public string? ContactName { get; set; }
    public string? ContactEmail { get; set; }
    public string? ContactPhone { get; set; }
    public string? ContactPosition { get; set; }
    public string? UpdatedBy { get; set; }

    // Navigation
    public CustomerProfile? Customer { get; set; }
    public Order? Order { get; set; }
    public ICollection<ComplaintMessage> Messages { get; set; } = new List<ComplaintMessage>();
}
