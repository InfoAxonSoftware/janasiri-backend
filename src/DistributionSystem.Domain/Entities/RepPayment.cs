using DistributionSystem.Domain.Enums;

namespace DistributionSystem.Domain.Entities;

public class RepPayment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RepId { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string? ReferenceNumber { get; set; }
    public decimal Amount { get; set; }
    public string? ImageUrl { get; set; }
    public RepPaymentStatus Status { get; set; } = RepPaymentStatus.AwaitingConfirmation;
    public string? AdminNotes { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public string? UpdatedBy { get; set; }

    // Role-independent soft-delete: each role's trash state is separate and
    // does not affect visibility for the other roles.
    public bool IsDeletedByAdmin { get; set; }
    public DateTime? AdminDeletedAt { get; set; }

    public bool IsDeletedByCoordinator { get; set; }
    public DateTime? CoordinatorDeletedAt { get; set; }

    public bool IsDeletedBySalesRep { get; set; }
    public DateTime? SalesRepDeletedAt { get; set; }

    // Navigation
    public SalesRepProfile Rep { get; set; } = null!;
}
