using DistributionSystem.Domain.Enums;
using DistributionSystem.Domain.Interfaces;

namespace DistributionSystem.Domain.Entities;

public class Quotation : IAuditableEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string QuotationNumber { get; set; } = string.Empty;
    public Guid CustomerId { get; set; }
    public Guid? RepId { get; set; }
    public Guid? CoordinatorId { get; set; }
    public QuotationStatus Status { get; set; } = QuotationStatus.Draft;
    public decimal SubTotal { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal TotalAmount { get; set; }
    public string? Notes { get; set; }
    public string? RejectionReason { get; set; }
    public DateTime? ValidUntil { get; set; }
    public Guid? ConvertedOrderId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public string? UpdatedBy { get; set; }

    // Soft-delete: admin trash
    public bool IsDeleted { get; set; } = false;
    public DateTime? DeletedAt { get; set; }
    public string? DeletedBy { get; set; }

    // Soft-delete: per-role trash (independent, auto-purge after 7 days)
    public bool IsDeletedByRep { get; set; } = false;
    public DateTime? RepDeletedAt { get; set; }
    public bool IsDeletedByCoordinator { get; set; } = false;
    public DateTime? CoordinatorDeletedAt { get; set; }
    public bool IsDeletedByCustomer { get; set; } = false;
    public DateTime? CustomerDeletedAt { get; set; }

    // Navigation
    public CustomerProfile Customer { get; set; } = null!;
    public SalesRepProfile? Rep { get; set; }
    public CoordinatorProfile? Coordinator { get; set; }
    public Order? ConvertedOrder { get; set; }
    public ICollection<QuotationItem> Items { get; set; } = new List<QuotationItem>();
}
