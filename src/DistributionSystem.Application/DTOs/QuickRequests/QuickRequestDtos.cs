using DistributionSystem.Domain.Enums;

namespace DistributionSystem.Application.DTOs.QuickRequests;

// ── Response DTOs ─────────────────────────────────────────────────────────────

public class QuickRequestDto
{
    public Guid Id { get; set; }
    public string RequestNumber { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;          // "Order" | "Quotation"
    public string CustomerName { get; set; } = string.Empty;
    public string Details { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? AdminNotes { get; set; }
    public Guid RepId { get; set; }
    public string RepName { get; set; } = string.Empty;
    public List<string> ImageUrls { get; set; } = [];
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public DateTime? DeletedAt { get; set; }
}

// ── Request DTOs ──────────────────────────────────────────────────────────────

public class CreateQuickRequestDto
{
    public string Type { get; set; } = string.Empty;          // "Order" | "Quotation"
    public string CustomerName { get; set; } = string.Empty;
    public string Details { get; set; } = string.Empty;
}

public class UpdateQuickRequestStatusDto
{
    public string Status { get; set; } = string.Empty;
    public string? AdminNotes { get; set; }
}
