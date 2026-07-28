using DistributionSystem.Domain.Enums;
using DistributionSystem.Domain.Interfaces;

namespace DistributionSystem.Domain.Entities;

public class Order : IAuditableEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string OrderNumber { get; set; } = string.Empty;
    public Guid CustomerId { get; set; }
    public Guid? RepId { get; set; }
    public DateTime OrderDate { get; set; } = DateTime.UtcNow;
    public DateTime? RequiredDeliveryDate { get; set; }
    public DateTime? ActualDeliveryDate { get; set; }
    public OrderStatus Status { get; set; } = OrderStatus.Pending;
    public decimal SubTotal { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal TotalAmount { get; set; }
    public string? DeliveryAddress { get; set; }
    public string? DeliveryNotes { get; set; }
    public Guid? ApprovedBy { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public string? CancellationReason { get; set; }
    public string? RejectionReason { get; set; }
    public int? Rating { get; set; }
    public string? RatingComment { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public string? UpdatedBy { get; set; }

    // Role-isolated trash. The legacy admin names are retained for schema compatibility.
    public bool IsDeleted { get; set; } = false;
    public DateTime? DeletedAt { get; set; }
    public string? DeletedBy { get; set; }
    public bool IsPurgedByAdmin { get; set; } = false;
    public DateTime? AdminPurgedAt { get; set; }

    // Per-role state is independent and remains until a manual role action.
    public bool IsDeletedByRep { get; set; } = false;
    public DateTime? RepDeletedAt { get; set; }
    public bool IsPurgedByRep { get; set; } = false;
    public DateTime? RepPurgedAt { get; set; }
    public bool IsDeletedByCoordinator { get; set; } = false;
    public DateTime? CoordinatorDeletedAt { get; set; }
    public bool IsPurgedByCoordinator { get; set; } = false;
    public DateTime? CoordinatorPurgedAt { get; set; }
    public bool IsDeletedByCustomer { get; set; } = false;
    public DateTime? CustomerDeletedAt { get; set; }

    // Navigation
    public CustomerProfile Customer { get; set; } = null!;
    public SalesRepProfile? Rep { get; set; }
    public ICollection<OrderItem> OrderItems { get; set; } = new List<OrderItem>();
    public ICollection<Payment> Payments { get; set; } = new List<Payment>();
}
