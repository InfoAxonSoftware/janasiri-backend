namespace DistributionSystem.Application.DTOs.Customer;

public class CustomerDto
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string ShopName { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string? CustomerType { get; set; }
    public string? BusinessRegistrationNumber { get; set; }
    public bool IsActive { get; set; }
    public string? Email { get; set; }
    public string? PhoneNumber { get; set; }
    public string? Street { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public bool MustChangePassword { get; set; }
    public string? TemporaryPassword { get; set; }
    public Guid? RegionId { get; set; }
    public string? RegionName { get; set; }
    public Guid? SubRegionId { get; set; }
    public string? SubRegionName { get; set; }
    public Guid? AssignedRepId { get; set; }
    public string? AssignedRepName { get; set; }
    public string? ApprovalStatus { get; set; }
    public string? ApprovalRejectionReason { get; set; }
    public Guid? AssignedCoordinatorId { get; set; }
    public string? AssignedCoordinatorName { get; set; }
    public int TotalOrders { get; set; }
    public decimal TotalOrderValue { get; set; }
    public DateTime CreatedAt { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
}

public class CreateCustomerRequest
{
    public string Username { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string Password { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public string ShopName { get; set; } = string.Empty;
    public string? CustomerType { get; set; } // "Tax" or "NonTax"
    public string? BusinessRegistrationNumber { get; set; }
    public string? Street { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? PostalCode { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public Guid? AssignedRepId { get; set; }
    public Guid? AssignedCoordinatorId { get; set; }
    public Guid? RegionId { get; set; }
    public Guid? SubRegionId { get; set; }
    /// <summary>Internal flag — set true by admin controller so the customer is immediately Approved</summary>
    public bool IsAdminCreated { get; set; } = false;
}

public class UpdateCustomerRequest
{
    public string? ShopName { get; set; }
    public string? PhoneNumber { get; set; }
    public string? Street { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public Guid? AssignedRepId { get; set; }
    public Guid? AssignedCoordinatorId { get; set; }
    public Guid? RegionId { get; set; }
    public Guid? SubRegionId { get; set; }

    // When true, clear the assignment even if AssignedRepId is null.
    public bool? ClearAssignedRep { get; set; }
    public bool? ClearAssignedCoordinator { get; set; }
}

public class PriceDetailDto
{
    public Guid ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal? SpecialPrice { get; set; }
    public decimal? DiscountPercent { get; set; }
}

public class SpecialPriceUpdateRequest
{
    public Guid ProductId { get; set; }
    public decimal? SpecialPrice { get; set; }
    public decimal? DiscountPercent { get; set; }
}

public class CustomerSummaryDto
{
    public CustomerDto Customer { get; set; } = null!;
    public decimal TotalPurchases { get; set; }
    public int TotalOrders { get; set; }
    public DateTime? LastOrderDate { get; set; }
    public List<string> FrequentProducts { get; set; } = [];
    public RegistrationSummaryDto? RegistrationRequest { get; set; }
}

public class RegistrationSummaryDto
{
    public string CustomerType { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public string? BusinessRegistrationNumber { get; set; }
    public string? RegisteredAddress { get; set; }
    public DateTime? IncorporateDate { get; set; }
    public string? BusinessName { get; set; }
    public string? BusinessLocation { get; set; }
    public string? Telephone { get; set; }
    public string? Email { get; set; }
    public string? BankBranch { get; set; }
    public string? Province { get; set; }
    public string? Town { get; set; }
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
    public string? BusinessRegDocPath { get; set; }
    public string? BusinessAddressDocPath { get; set; }
    public string? VatDocPath { get; set; }
}

public class CustomerFilterOptionsDto
{
    public List<RepOptionDto> AssignedReps { get; set; } = [];
    public List<CoordinatorOptionDto> Coordinators { get; set; } = [];
    public List<RegionOptionDto> Regions { get; set; } = [];
    public List<SubRegionOptionDto> SubRegions { get; set; } = [];
}

public class CoordinatorOptionDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

public class RegionOptionDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

public class SubRegionOptionDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public Guid RegionId { get; set; }
}

public class RepOptionDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

// ── Registration Request DTOs ──────────────────────────────────

/// <summary>Used when an admin creates a customer directly (bypasses Pending → auto-approves).</summary>
public class AdminCreateRegistrationRequest
{
    public string CustomerType { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public string BusinessRegistrationNumber { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string? RegisteredAddress { get; set; }
    public DateTime? IncorporateDate { get; set; }
    public string? BusinessName { get; set; }
    public string? BusinessLocation { get; set; }
    public string Telephone { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? BankBranch { get; set; }
    public string? Province { get; set; }
    public string? Town { get; set; }
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
    // Assignment fields set by admin
    public Guid? RegionId { get; set; }
    public Guid? SubRegionId { get; set; }
    public Guid? AssignedCoordinatorId { get; set; }
    public Guid? AssignedRepId { get; set; }
    // Files are received separately via IFormFile
}

public class SubmitRegistrationFormRequest
{
    public string CustomerType { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public string BusinessRegistrationNumber { get; set; } = string.Empty;
    public string? RegisteredAddress { get; set; }
    public DateTime? IncorporateDate { get; set; }
    public string? BusinessName { get; set; }
    public string? BusinessLocation { get; set; }
    public string Telephone { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? BankBranch { get; set; }
    public string? Province { get; set; }
    public string? Town { get; set; }
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
    public Guid? RegionId { get; set; }
    public Guid? SubRegionId { get; set; }
    // Files are received separately via IFormFile
    public string? PreferredUsername { get; set; }
    public string? PreferredPassword { get; set; }
}

public class UpdateCustomerRegistrationDetailsRequest
{
    public string? CustomerType { get; set; }
    public string? CustomerName { get; set; }
    public string? BusinessRegistrationNumber { get; set; }
    public string? RegisteredAddress { get; set; }
    public DateTime? IncorporateDate { get; set; }
    public string? BusinessName { get; set; }
    public string? BusinessLocation { get; set; }
    public string? Telephone { get; set; }
    public string? Email { get; set; }
    public string? BankBranch { get; set; }
    public string? Province { get; set; }
    public string? Town { get; set; }
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
    public Guid? RegionId { get; set; }
    public Guid? SubRegionId { get; set; }
    public Guid? AssignedCoordinatorId { get; set; }
}

public class ReviewRegistrationRequest
{
    public string Action { get; set; } = string.Empty; // "Approve" or "Reject"
    public string? Username { get; set; }
    public string? Password { get; set; }
    public string? RejectionReason { get; set; }
    public string? ReviewNotes { get; set; }
    public Guid? RegionId { get; set; }
    public Guid? SubRegionId { get; set; }
    public Guid? AssignedCoordinatorId { get; set; }
    public Guid? AssignedRepId { get; set; }
}

public class CustomerRegistrationRequestDto
{
    public Guid Id { get; set; }
    public string CustomerType { get; set; } = string.Empty;
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
    public string? BusinessRegDocUrl { get; set; }
    public string? BusinessAddressDocUrl { get; set; }
    public string? VatDocUrl { get; set; }
    public string? PreferredUsername { get; set; }
    public string? PreferredPassword { get; set; }
    public string Status { get; set; } = "Pending";
    public string? RejectionReason { get; set; }
    public string? ReviewNotes { get; set; }
    public Guid? RegionId { get; set; }
    public string? RegionName { get; set; }
    public Guid? SubRegionId { get; set; }
    public string? SubRegionName { get; set; }
    public Guid? AssignedCoordinatorId { get; set; }
    public string? AssignedCoordinatorName { get; set; }
    public Guid? AssignedRepId { get; set; }
    public string? AssignedRepName { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public DateTime CreatedAt { get; set; }
}
