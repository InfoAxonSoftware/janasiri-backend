using DistributionSystem.Domain.Enums;

namespace DistributionSystem.Domain.Entities;

public class QuickRequest
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string RequestNumber { get; set; } = string.Empty;  // e.g. QO-0001 / QQ-0001
    public QuickRequestType Type { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string Details { get; set; } = string.Empty;
    public QuickRequestStatus Status { get; set; } = QuickRequestStatus.Pending;
    public string? AdminNotes { get; set; }

    public Guid? RepId { get; set; }
    public SalesRepProfile? Rep { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public string? UpdatedBy { get; set; }

    // Role-isolated manual trash. Purging removes only the current role's view.
    public bool IsDeletedByAdmin { get; set; } = false;
    public DateTime? AdminDeletedAt { get; set; }
    public bool IsPurgedByAdmin { get; set; } = false;
    public DateTime? AdminPurgedAt { get; set; }
    public bool IsDeletedByRep { get; set; } = false;
    public DateTime? RepDeletedAt { get; set; }
    public bool IsPurgedByRep { get; set; } = false;
    public DateTime? RepPurgedAt { get; set; }
    public bool IsDeletedByCoordinator { get; set; } = false;
    public DateTime? CoordinatorDeletedAt { get; set; }
    public bool IsPurgedByCoordinator { get; set; } = false;
    public DateTime? CoordinatorPurgedAt { get; set; }

    public ICollection<QuickRequestImage> Images { get; set; } = [];
    public ICollection<QuickRequestAttachment> Attachments { get; set; } = [];
}
