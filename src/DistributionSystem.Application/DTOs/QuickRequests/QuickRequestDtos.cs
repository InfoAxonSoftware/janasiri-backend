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


public class GuestOrderRepOptionDto
{
    public Guid Id { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string EmployeeCode { get; set; } = string.Empty;
}

public class AssignGuestOrderRepDto
{
    [System.Text.Json.Serialization.JsonRequired]
    public Guid? RepId { get; set; }
    [System.Text.Json.Serialization.JsonRequired]
    public Guid? ExpectedRepId { get; set; }
}

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

public class GuestQuickOrderTrackingRequestDto
{
    public string RequestNumber { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
}

public class GuestQuickOrderAttachmentAccessDto
{
    public string PhoneNumber { get; set; } = string.Empty;
}

public class GuestQuickOrderTrackingAttachmentDto
{
    public Guid Id { get; set; }
    public string OriginalFileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public DateTime UploadedAt { get; set; }
}

public class GuestQuickOrderTrackingDto
{
    public string RequestNumber { get; set; } = string.Empty;
    public string BusinessName { get; set; } = string.Empty;
    public string ContactPerson { get; set; } = string.Empty;
    public string ContactNumberMasked { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public List<GuestQuickOrderItemDto> Items { get; set; } = [];
    public List<GuestQuickOrderTrackingAttachmentDto> Attachments { get; set; } = [];
}
public class GuestQuickOrderItemDto
{
    public string Description { get; set; } = string.Empty;
    public string? Size { get; set; }
    public string? Brand { get; set; }
    public int Quantity { get; set; }
}

public class CreateGuestQuickOrderDto
{
    public string BusinessName { get; set; } = string.Empty;
    public string ContactPerson { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public List<GuestQuickOrderItemDto> Items { get; set; } = [];
}
