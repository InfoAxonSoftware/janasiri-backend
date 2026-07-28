using DistributionSystem.Domain.Enums;
using DistributionSystem.Domain.Interfaces;

namespace DistributionSystem.Domain.Entities;

public class Payment : IAuditableEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CustomerId { get; set; }
    public Guid? OrderId { get; set; }
    public DateTime PaymentDate { get; set; } = DateTime.UtcNow;
    public decimal Amount { get; set; }
    public PaymentMethod PaymentMethod { get; set; }
    public string? ReferenceNumber { get; set; }
    public string? ChequeNumber { get; set; }
    public string? BankName { get; set; }
    public DateTime? ChequeDate { get; set; }
    public Guid? CollectedByRepId { get; set; }
    public Guid? VerifiedByAdminId { get; set; }
    public PaymentStatus Status { get; set; } = PaymentStatus.Pending;
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public string? UpdatedBy { get; set; }

    // Navigation
    public CustomerProfile Customer { get; set; } = null!;
    public Order? Order { get; set; }
    public SalesRepProfile? CollectedByRep { get; set; }
    public ICollection<PaymentAllocation> Allocations { get; set; } = new List<PaymentAllocation>();
}
