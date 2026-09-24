using DistributionSystem.Domain.Enums;

namespace DistributionSystem.Application.DTOs.QuickRequests;

// ── Response DTOs ─────────────────────────────────────────────────────────────

public class QuickRequestAttachmentDto
{
    public Guid Id { get; set; }
    public string Url { get; set; } = string.Empty;
    public string OriginalFileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public DateTime UploadedAt { get; set; }
}

public class QuickRequestDto
{
    public Guid Id { get; set; }
    public string RequestNumber { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;          // "Order" | "Quotation"
    public string CustomerName { get; set; } = string.Empty;
    public string Details { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? AdminNotes { get; set; }
    public Guid? RepId { get; set; }
    public string RepName { get; set; } = string.Empty;
    public string? CreatedBy { get; set; }

    // Existing image URLs kept for backward compatibility
    public List<string> ImageUrls { get; set; } = [];

    // New generic attachments: images + PDFs
    public List<QuickRequestAttachmentDto> Attachments { get; set; } = [];

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