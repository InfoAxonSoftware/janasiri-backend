namespace DistributionSystem.Application.DTOs.Support;

public class ComplaintDto
{
    public Guid Id { get; set; }
    public Guid? CustomerId { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public Guid CreatedByUserId { get; set; }
    public string CreatedByRole { get; set; } = string.Empty;
    public string CreatedByName { get; set; } = string.Empty;
    public Guid? AssignedTo { get; set; }
    public Guid? OrderId { get; set; }
    public string Subject { get; set; } = string.Empty;
    public string TicketType { get; set; } = "Complaint";
    public string Description { get; set; } = string.Empty;
    public string? ContactName { get; set; }
    public string? ContactEmail { get; set; }
    public string? ContactPhone { get; set; }
    public string? ContactPosition { get; set; }
    public string Priority { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? ResolvedAt { get; set; }
}

public class CreateComplaintRequest
{
    public Guid? OrderId { get; set; }
    public string Subject { get; set; } = string.Empty;
    public string TicketType { get; set; } = "Complaint";
    public string Description { get; set; } = string.Empty;
    public string? ContactName { get; set; }
    public string? ContactEmail { get; set; }
    public string? ContactPhone { get; set; }
    public string? ContactPosition { get; set; }
    public string Priority { get; set; } = "Medium";
}

public class ComplaintMessageDto
{
    public Guid Id { get; set; }
    public Guid ComplaintId { get; set; }
    public Guid SenderUserId { get; set; }
    public string SenderRole { get; set; } = string.Empty;
    public string SenderName { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public bool IsSystemMessage { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class SendComplaintMessageRequest
{
    public string Message { get; set; } = string.Empty;
}

public class FeedbackRequest
{
    public string Subject { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}

public class PromotionDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string PromotionType { get; set; } = string.Empty;
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public decimal? DiscountPercent { get; set; }
    public int? BuyQuantity { get; set; }
    public int? GetQuantity { get; set; }
    public bool IsActive { get; set; }
}
