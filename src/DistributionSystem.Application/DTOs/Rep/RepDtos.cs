namespace DistributionSystem.Application.DTOs.Rep;

public class RepDto
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string Username { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string EmployeeCode { get; set; } = string.Empty;
    public DateTime HireDate { get; set; }
    public List<Guid> RegionIds { get; set; } = [];
    public List<string> RegionNames { get; set; } = [];
    public List<Guid> SubRegionIds { get; set; } = [];
    public List<string> SubRegionNames { get; set; } = [];
    public List<Guid> CoordinatorIds { get; set; } = [];
    public List<string> CoordinatorNames { get; set; } = [];
    public string? Email { get; set; }
    public string? PhoneNumber { get; set; }
    public bool IsActive { get; set; }
    public bool MustChangePassword { get; set; }
    public string? TemporaryPassword { get; set; }
    public int AssignedCustomersCount { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class CreateRepRequest
{
    public string? Username { get; set; }
    public string? Email { get; set; }
    public string Password { get; set; } = string.Empty;
    public string? PhoneNumber { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string EmployeeCode { get; set; } = string.Empty;
    public DateTime HireDate { get; set; }
    public List<Guid> RegionIds { get; set; } = [];
    public List<Guid> SubRegionIds { get; set; } = [];
    public List<Guid> CoordinatorIds { get; set; } = [];
}

public class UpdateRepRequest
{
    public string? FullName { get; set; }
    public string? EmployeeCode { get; set; }
    public DateTime? HireDate { get; set; }
    public string? PhoneNumber { get; set; }
    public string? Email { get; set; }
    public List<Guid>? RegionIds { get; set; }
    public List<Guid>? SubRegionIds { get; set; }
    public List<Guid>? CoordinatorIds { get; set; }
    public bool? IsActive { get; set; }
}

public class RepPerformanceDto
{
    public Guid RepId { get; set; }
    public string RepName { get; set; } = string.Empty;
    public decimal TotalSales { get; set; }
    public int TotalOrders { get; set; }
    public int TotalCustomers { get; set; }
    public int CustomersVisited { get; set; }
    public decimal CollectedPayments { get; set; }
    public decimal TargetAmount { get; set; }
    public decimal AchievedAmount { get; set; }
    public decimal AchievementPercentage { get; set; }
}

public class VisitDto
{
    public Guid Id { get; set; }
    public Guid CustomerId { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string? ShopName { get; set; }
    public DateTime PlannedDate { get; set; }
    public DateTime? ActualStartTime { get; set; }
    public DateTime? ActualEndTime { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? Notes { get; set; }
    public string? OutcomeReason { get; set; }
    public int OrdersPlaced { get; set; }
    public decimal PaymentsCollected { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    // contact/address info
    public string? PhoneNumber { get; set; }
    public string? Street { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? PostalCode { get; set; }
    public string? Country { get; set; }
}

public class CheckInRequest
{
    public Guid CustomerId { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }
}

public class CheckOutRequest
{
    public Guid VisitId { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public string? Notes { get; set; }
    public string? OutcomeReason { get; set; }
}

public class AdHocVisitRequest
{
    public Guid CustomerId { get; set; }
    public string? Notes { get; set; }
}

public class RouteProgressDto
{
    public Guid RepId { get; set; }
    public string RepName { get; set; } = string.Empty;
    public int TotalPlanned { get; set; }
    public int Completed { get; set; }
    public double StrikeRate { get; set; }
}

public class RouteDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    // Backward compatibility fields; represents the first assigned rep when present.
    public Guid? RepId { get; set; }
    public string? RepName { get; set; }
    public List<RouteRepDto> AssignedReps { get; set; } = [];
    public List<string> DaysOfWeek { get; set; } = [];
    public int EstimatedDurationMinutes { get; set; }
    public bool IsActive { get; set; }
    public List<RouteCustomerDto> Customers { get; set; } = [];
}

public class RouteRepDto
{
    public Guid RepId { get; set; }
    public string RepName { get; set; } = string.Empty;
}

public class RouteCustomerDto
{
    public Guid CustomerId { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string? ShopName { get; set; }
    public int VisitOrder { get; set; }
    public string? VisitFrequency { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
}

public class CreateRouteRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public Guid? RepId { get; set; }
    public string DaysOfWeek { get; set; } = "[]";
    public int EstimatedDurationMinutes { get; set; }
    public List<AddRouteCustomerRequest> Customers { get; set; } = [];
}

public class AddRouteCustomerRequest
{
    public Guid CustomerId { get; set; }
    public int VisitOrder { get; set; }
    public string? VisitFrequency { get; set; } = "Weekly";
}

public class AssignRouteRequest
{
    public Guid RepId { get; set; }
}

public class SalesTargetDto
{
    public Guid Id { get; set; }
    public Guid RepId { get; set; }
    public string? TargetName { get; set; }
    public string TargetPeriod { get; set; } = string.Empty;
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public DateTime CreatedAt { get; set; }
    public decimal TargetAmount { get; set; }
    public decimal AchievedAmount { get; set; }
    public decimal AchievementPercentage => TargetAmount > 0 ? (AchievedAmount / TargetAmount) * 100 : 0;
    public decimal BalanceRemaining => Math.Max(TargetAmount - AchievedAmount, 0);
    public decimal ExceededBy => Math.Max(AchievedAmount - TargetAmount, 0);
    public string Status { get; set; } = string.Empty;

    // ── Current uploaded sales report (null until the first report is uploaded) ──
    public bool HasReport { get; set; }
    public Guid? CurrentReportId { get; set; }
    public DateTime? ReportFromDate { get; set; }
    public DateTime? ReportAsAtDate { get; set; }
    public string? ReportSourceFileName { get; set; }
    public DateTime? ReportUploadedAt { get; set; }
    public int DistinctOrderCount { get; set; }
    public int DistinctCustomerCount { get; set; }

    /// <summary>Below Target | On Track | Target Achieved | Target Exceeded</summary>
    public string PerformanceStatus => !HasReport
        ? "Below Target"
        : AchievedAmount > TargetAmount ? "Target Exceeded"
        : AchievedAmount == TargetAmount && TargetAmount > 0 ? "Target Achieved"
        : AchievementPercentage >= 75 ? "On Track"
        : "Below Target";
}

public class LeaderboardDto
{
    public int Rank { get; set; }
    public Guid RepId { get; set; }
    public string RepName { get; set; } = string.Empty;
    public decimal TotalSales { get; set; }
    public int TotalOrders { get; set; }
}