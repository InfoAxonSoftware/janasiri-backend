using DistributionSystem.Domain.Enums;
using DistributionSystem.Domain.Interfaces;
using DistributionSystem.Domain.ValueObjects;

namespace DistributionSystem.Domain.Entities;

public class CustomerProfile : IAuditableEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public string ShopName { get; set; } = string.Empty;
    public string? BusinessRegistrationNumber { get; set; }
    public Address Address { get; set; } = new();
    public Location? Location { get; set; }
    public Guid? RegionId { get; set; }
    public Guid? SubRegionId { get; set; }
    public Guid? AssignedRepId { get; set; }
    public Guid? AssignedCoordinatorId { get; set; }
    public CustomerApprovalStatus ApprovalStatus { get; set; } = CustomerApprovalStatus.PendingApproval;
    public string? ApprovalRejectionReason { get; set; }
    public Guid? ApprovedByCoordinatorId { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public bool IsDeleted { get; set; } = false;
    public DateTime? DeletedAt { get; set; }
    public string? DeletedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public string? UpdatedBy { get; set; }

    // Navigation
    public User User { get; set; } = null!;
    public Region? Region { get; set; }
    public SubRegion? SubRegion { get; set; }
    public SalesRepProfile? AssignedRep { get; set; }
    public CoordinatorProfile? AssignedCoordinator { get; set; }
    public ICollection<Order> Orders { get; set; } = new List<Order>();
    public ICollection<Payment> Payments { get; set; } = new List<Payment>();
    public ICollection<Complaint> Complaints { get; set; } = new List<Complaint>();
    public ICollection<CartItem> CartItems { get; set; } = new List<CartItem>();
    public ICollection<FavoriteProduct> FavoriteProducts { get; set; } = new List<FavoriteProduct>();
    public ICollection<Quotation> Quotations { get; set; } = new List<Quotation>();
}
