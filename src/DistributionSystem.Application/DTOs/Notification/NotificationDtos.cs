namespace DistributionSystem.Application.DTOs.Notification;

public class NotificationDto
{
    public Guid Id { get; set; }
    public string NotificationType { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public bool IsRead { get; set; }
    public DateTime? ReadAt { get; set; }
    // Use DateTimeOffset so the serializer emits an explicit timezone and
    // the client can parse it correctly instead of assuming local.
    public DateTimeOffset CreatedAt { get; set; }
    public string? Metadata { get; set; }
}

public class SendNotificationRequest
{
    public Guid UserId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string Type { get; set; } = "General";
    public string? Metadata { get; set; }
}

public class BroadcastNotificationRequest
{
    public string Role { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string Type { get; set; } = "General";
    public string? Metadata { get; set; }
}

public class NotificationPreferencesDto
{
    public bool EmailEnabled { get; set; } = true;
    public bool SmsEnabled { get; set; }
    public bool PushEnabled { get; set; } = true;
    public bool InAppEnabled { get; set; } = true;
}
