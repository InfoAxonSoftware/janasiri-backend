using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using DistributionSystem.Application.Services.Interfaces;
using System.Security.Claims;

namespace DistributionSystem.API.Hubs;

[Authorize]
public class NotificationHub : Hub
{
    public override async Task OnConnectedAsync()
    {
        var userId = Context.UserIdentifier;
        if (userId != null)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, $"user_{userId}");
            var role = Context.User?.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;
            if (role != null)
                await Groups.AddToGroupAsync(Context.ConnectionId, $"role_{role}");
        }
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var userId = Context.UserIdentifier;
        if (userId != null)
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"user_{userId}");
        await base.OnDisconnectedAsync(exception);
    }
}

[Authorize]
public class OrderTrackingHub : Hub
{
    public async Task JoinOrderGroup(string orderId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, $"order_{orderId}");
    }

    public async Task LeaveOrderGroup(string orderId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"order_{orderId}");
    }
}

[Authorize]
public class SupportHub : Hub
{
    private readonly ISupportService _supportService;

    public SupportHub(ISupportService supportService)
    {
        _supportService = supportService;
    }

    private Guid GetUserId() => Guid.Parse(Context.User?.FindFirstValue(ClaimTypes.NameIdentifier) ?? throw new HubException("Unauthorized"));
    private string GetRole() => Context.User?.FindFirstValue(ClaimTypes.Role) ?? string.Empty;

    public async Task JoinComplaintGroup(string complaintId)
    {
        if (!Guid.TryParse(complaintId, out var parsedId))
            throw new HubException("Invalid complaint id");

        await _supportService.EnsureComplaintAccessAsync(GetUserId(), GetRole(), parsedId, CancellationToken.None);
        await Groups.AddToGroupAsync(Context.ConnectionId, $"complaint_{parsedId}");
    }

    public async Task LeaveComplaintGroup(string complaintId)
    {
        if (!Guid.TryParse(complaintId, out var parsedId))
            return;

        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"complaint_{parsedId}");
    }
}

