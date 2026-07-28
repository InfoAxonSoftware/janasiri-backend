using DistributionSystem.Application.DTOs.Common;

namespace DistributionSystem.Application.DTOs.RepPayments;

public class RepPaymentDto
{
    public Guid Id { get; set; }
    public string ReportNumber { get; set; } = string.Empty;
    public Guid RepId { get; set; }
    public string RepName { get; set; } = string.Empty;
    public string? CoordinatorName { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string? ReferenceNumber { get; set; }
    public decimal Amount { get; set; }
    public string? ImageUrl { get; set; }
    public bool HasEvidence { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? AdminNotes { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }

    // Kept for backward compatibility with the existing admin trash UI (mirrors AdminDeletedAt).
    public DateTime? DeletedAt { get; set; }

    public bool IsDeletedByAdmin { get; set; }
    public DateTime? AdminDeletedAt { get; set; }
    public bool IsDeletedByCoordinator { get; set; }
    public DateTime? CoordinatorDeletedAt { get; set; }
    public bool IsDeletedBySalesRep { get; set; }
    public DateTime? SalesRepDeletedAt { get; set; }
}

public class CreateRepPaymentDto
{
    public string CustomerName { get; set; } = string.Empty;
    public string ReferenceNumber { get; set; } = string.Empty;
    public decimal Amount { get; set; }
}

public class UpdateRepPaymentStatusDto
{
    public string Status { get; set; } = string.Empty;
    public string? AdminNotes { get; set; }
}


public class RepPaymentQueryDto
{
    private const int MaxPageSize = 500;

    private int _page = 1;
    private int _pageSize = 20;

    public int Page
    {
        get => _page;
        set => _page = value < 1 ? 1 : value;
    }

    public int PageSize
    {
        get => _pageSize;
        set => _pageSize = value < 1 ? 20 : Math.Min(value, MaxPageSize);
    }

    public string? Search { get; set; }
    public string? Status { get; set; }
    public Guid? RepId { get; set; }
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
    public string SortField { get; set; } = "createdAt";
    public string SortDir { get; set; } = "desc";
    public string View { get; set; } = "active";
}

public class RepPaymentRepOptionDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
}
