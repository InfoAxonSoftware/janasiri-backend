using DistributionSystem.Application.DTOs.Notification;
using DistributionSystem.Application.DTOs.RepPayments;
using DistributionSystem.Application.Services.Interfaces;
using DistributionSystem.API.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace DistributionSystem.API.Services;

public class NotificationPublisher : INotificationPublisher
{
    private readonly IHubContext<NotificationHub> _hubContext;

    public NotificationPublisher(IHubContext<NotificationHub> hubContext)
    {
        _hubContext = hubContext;
    }

    public Task PublishToUserAsync(Guid userId, NotificationDto notification, CancellationToken cancellationToken = default)
    {
        // Send to specific user group (added in NotificationHub.OnConnectedAsync)
        return _hubContext.Clients.Group($"user_{userId}").SendAsync("ReceiveNotification", notification, cancellationToken);
    }

    public Task PublishToRoleAsync(string role, object payload, CancellationToken cancellationToken = default)
    {
        // Send to role group (added in NotificationHub.OnConnectedAsync)
        return _hubContext.Clients.Group($"role_{role}").SendAsync("ReceiveNotification", payload, cancellationToken);
    }

    public Task PublishPaymentReportEventAsync(PaymentReportEventDto evt, IEnumerable<Guid> targetUserIds, CancellationToken cancellationToken = default)
    {
        var groups = targetUserIds.Distinct().Select(id => $"user_{id}").ToList();
        if (groups.Count == 0) return Task.CompletedTask;

        return _hubContext.Clients.Groups(groups).SendAsync("PaymentReportEvent", evt, cancellationToken);
    }
}
