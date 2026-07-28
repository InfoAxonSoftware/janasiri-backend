namespace DistributionSystem.Application.DTOs.Coordinator;

public class CoordinatorDto
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string Username { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string EmployeeCode { get; set; } = string.Empty;
    public Guid? RegionId { get; set; }
    public string? RegionName { get; set; }
    public DateTime HireDate { get; set; }
    public string? Email { get; set; }
    public string? PhoneNumber { get; set; }
    public bool IsActive { get; set; }
    public bool MustChangePassword { get; set; }
    public string? TemporaryPassword { get; set; }
    public DateTime CreatedAt { get; set; }
    public int AssignedRepsCount { get; set; }
    public int AssignedCustomersCount { get; set; }
}

public class CreateCoordinatorRequest
{
    public string? Username { get; set; }
    public string? Email { get; set; }
    public string Password { get; set; } = string.Empty;
    public string? PhoneNumber { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string EmployeeCode { get; set; } = string.Empty;
    public Guid? RegionId { get; set; }
    public DateTime HireDate { get; set; }
}

public class UpdateCoordinatorRequest
{
    public string? FullName { get; set; }
    public string? EmployeeCode { get; set; }
    public string? PhoneNumber { get; set; }
    public string? Email { get; set; }
    public DateTime? HireDate { get; set; }
    public Guid? RegionId { get; set; }
    public bool? IsActive { get; set; }
}

public class CoordinatorDashboardDto
{
    public int TotalReps { get; set; }
    public int TotalCustomers { get; set; }
    public int PendingCustomerApprovals { get; set; }
    public int PendingQuotations { get; set; }
    public decimal TotalSalesThisMonth { get; set; }
    public int TotalOrdersThisMonth { get; set; }
    public List<PendingCustomerApprovalDto> RecentPendingApprovals { get; set; } = [];
    public List<PendingQuotationDto> RecentPendingQuotations { get; set; } = [];
}

public class PendingCustomerApprovalDto
{
    public Guid CustomerId { get; set; }
    public string ShopName { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? PhoneNumber { get; set; }
    public string? RepName { get; set; }
    public string? City { get; set; }
    public DateTime RequestedAt { get; set; }
}

public class PendingQuotationDto
{
    public Guid QuotationId { get; set; }
    public string QuotationNumber { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public string? RepName { get; set; }
    public decimal TotalAmount { get; set; }
    public DateTime SubmittedAt { get; set; }
}

public class ApproveCustomerRequest
{
    public Guid? AssignedRepId { get; set; }
}

public class RejectCustomerRequest
{
    public string Reason { get; set; } = string.Empty;
}

public class AssignRepToCoordinatorRequest
{
    public Guid RepId { get; set; }
}
