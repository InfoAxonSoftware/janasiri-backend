using DistributionSystem.Domain.Interfaces;

namespace DistributionSystem.Domain.Entities;

public class CustomerRegistrationRequest : IAuditableEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();

    // Customer classification
    public string CustomerType { get; set; } = "NonTax"; // "Tax" or "NonTax"

    // General info
    public string CustomerName { get; set; } = string.Empty;
    public string? BusinessRegistrationNumber { get; set; }
    public string? RegisteredAddress { get; set; }
    public DateTime? IncorporateDate { get; set; }
    public string? BusinessName { get; set; }
    public string? BusinessLocation { get; set; }
    public string Telephone { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? BankBranch { get; set; }
    public string? Province { get; set; }
    public string? Town { get; set; }

    // Professional contacts
    public string? ProprietorName { get; set; }
    public string? ProprietorTp { get; set; }
    public string? ProprietorEmail { get; set; }
    public string? ManagerName { get; set; }
    public string? ManagerTp { get; set; }
    public string? ManagerEmail { get; set; }
    public string? ChefName { get; set; }
    public string? ChefTp { get; set; }
    public string? ChefEmail { get; set; }
    public string? PurchasingName { get; set; }
    public string? PurchasingTp { get; set; }
    public string? PurchasingEmail { get; set; }
    public string? AccountantName { get; set; }
    public string? AccountantTp { get; set; }
    public string? AccountantEmail { get; set; }

    // Uploaded document paths (relative to uploads directory)
    public string? BusinessRegDocPath { get; set; }
    public string? BusinessAddressDocPath { get; set; }
    public string? VatDocPath { get; set; }

    // Customer's preferred login credentials (suggestions for admin to use or override)
    public string? PreferredUsername { get; set; }
    public string? PreferredPassword { get; set; }

    // Admin review
    public string Status { get; set; } = "Pending"; // Pending | Approved | Rejected
    public string? RejectionReason { get; set; }
    public string? ReviewNotes { get; set; }
    public Guid? AssignedCoordinatorId { get; set; }
    public Guid? AssignedRepId { get; set; }
    public Guid? RegionId { get; set; }
    public Guid? SubRegionId { get; set; }
    public Guid? ReviewedByAdminId { get; set; }
    public DateTime? ReviewedAt { get; set; }

    // Audit
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public string? UpdatedBy { get; set; }

    // Navigation
    public CoordinatorProfile? AssignedCoordinator { get; set; }
    public SalesRepProfile? AssignedRep { get; set; }
    public Region? Region { get; set; }
    public SubRegion? SubRegion { get; set; }
}
