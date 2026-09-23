using System.Security.Claims;
using DistributionSystem.Application.DTOs.Common;
using DistributionSystem.Application.DTOs.Notification;
using DistributionSystem.Application.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DistributionSystem.API.Controllers;

/// <summary>
/// Notification endpoints
/// </summary>
[ApiController]
[Route("api")]
[Authorize]
[Produces("application/json")]
public class NotificationController : ControllerBase
{
    private readonly INotificationService _notificationService;
    public NotificationController(INotificationService notificationService)
    {
        _notificationService = notificationService;
    }

    private Guid GetUserId() => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    /// <summary>Get my notifications</summary>
    [HttpGet("notifications")]
    public async Task<IActionResult> GetMyNotifications([FromQuery] int page = 1, [FromQuery] int pageSize = 20,
        [FromQuery] bool unreadOnly = false, CancellationToken ct = default)
    {
        var userId = GetUserId();
        var result = await _notificationService.GetByUserAsync(userId, page, pageSize, unreadOnly, ct);
        return Ok(ApiResponse<PagedResult<NotificationDto>>.SuccessResponse(result));
    }

    /// <summary>Get unread notification count</summary>
    [HttpGet("notifications/unread-count")]
    public async Task<IActionResult> GetUnreadCount(CancellationToken ct)
    {
        var userId = GetUserId();
        var count = await _notificationService.GetUnreadCountAsync(userId, ct);
        return Ok(ApiResponse<int>.SuccessResponse(count));
    }

    /// <summary>Mark notification as read</summary>
    [HttpPut("notifications/{id}/read")]
    public async Task<IActionResult> MarkAsRead(Guid id, CancellationToken ct)
    {
        await _notificationService.MarkAsReadAsync(id, GetUserId(), ct);
        return Ok(ApiResponse<string>.SuccessResponse("Notification marked as read"));
    }

    /// <summary>Mark all notifications as read</summary>
    [HttpPut("notifications/read-all")]
    public async Task<IActionResult> MarkAllAsRead(CancellationToken ct)
    {
        var userId = GetUserId();
        await _notificationService.MarkAllAsReadAsync(userId, ct);
        return Ok(ApiResponse<string>.SuccessResponse("All notifications marked as read"));
    }

    /// <summary>Send notification to user (Admin)</summary>
    [HttpPost("admin/notifications/send")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public async Task<IActionResult> SendNotification([FromBody] SendNotificationRequest request, CancellationToken ct)
    {
        var result = await _notificationService.SendToUserAsync(request, ct);
        return Ok(ApiResponse<NotificationDto>.SuccessResponse(result, "Notification sent"));
    }

    /// <summary>Get active users available as notification recipients (Admin)</summary>
    [HttpGet("admin/notifications/recipients")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public async Task<IActionResult> GetNotificationRecipients(CancellationToken ct)
    {
        var recipients = await _notificationService.GetActiveRecipientsAsync(ct);
        return Ok(ApiResponse<IReadOnlyList<NotificationRecipientDto>>.SuccessResponse(recipients));
    }

    /// <summary>Send notification to role (Admin)</summary>
    [HttpPost("admin/notifications/broadcast")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public async Task<IActionResult> BroadcastNotification([FromBody] BroadcastNotificationRequest request, CancellationToken ct)
    {
        await _notificationService.SendBroadcastAsync(request, ct);
        return Ok(ApiResponse<string>.SuccessResponse("Notification broadcast sent"));
    }
}

