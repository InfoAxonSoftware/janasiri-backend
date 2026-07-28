using System.Security.Claims;
using DistributionSystem.Application.DTOs.Common;
using DistributionSystem.Application.DTOs.Notification;
using DistributionSystem.Application.Services.Interfaces;
using DistributionSystem.API.Hubs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;

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
    private readonly IHubContext<NotificationHub> _hubContext;

    public NotificationController(INotificationService notificationService, IHubContext<NotificationHub> hubContext)
    {
        _notificationService = notificationService;
        _hubContext = hubContext;
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
        await _notificationService.MarkAsReadAsync(id, ct);
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
        // Push via SignalR
        await _hubContext.Clients.Group($"user_{request.UserId}").SendAsync("ReceiveNotification", result, ct);
        return Ok(ApiResponse<NotificationDto>.SuccessResponse(result, "Notification sent"));
    }

    /// <summary>Send notification to role (Admin)</summary>
    [HttpPost("admin/notifications/broadcast")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public async Task<IActionResult> BroadcastNotification([FromBody] BroadcastNotificationRequest request, CancellationToken ct)
    {
        await _notificationService.SendToRoleAsync(request, ct);
        // Push via SignalR
        await _hubContext.Clients.Group($"role_{request.Role}").SendAsync("ReceiveNotification",
            new { request.Title, request.Message, request.Type }, ct);
        return Ok(ApiResponse<string>.SuccessResponse("Notification broadcast sent"));
    }
}

