using DistributionSystem.Domain.Interfaces;

namespace DistributionSystem.Domain.Entities;

public class ComplaintMessage : IAuditableEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ComplaintId { get; set; }
    public Guid SenderUserId { get; set; }
    public string SenderRole { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public bool IsSystemMessage { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public string? UpdatedBy { get; set; }

    public Complaint Complaint { get; set; } = null!;
    public User SenderUser { get; set; } = null!;
}
