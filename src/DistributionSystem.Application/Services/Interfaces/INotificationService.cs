using DistributionSystem.Application.DTOs.Common;
using DistributionSystem.Application.DTOs.Notification;
using DistributionSystem.Domain.Enums;

namespace DistributionSystem.Application.Services.Interfaces;

public interface INotificationService
{
    Task<PagedResult<NotificationDto>> GetByUserAsync(Guid userId, int page, int pageSize, bool unreadOnly = false, CancellationToken cancellationToken = default);
    Task<int> GetUnreadCountAsync(Guid userId, CancellationToken cancellationToken = default);
    Task MarkAsReadAsync(Guid notificationId, CancellationToken cancellationToken = default);
    Task MarkAllAsReadAsync(Guid userId, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid notificationId, CancellationToken cancellationToken = default);
    Task<NotificationDto> SendToUserAsync(SendNotificationRequest request, CancellationToken cancellationToken = default);
    Task SendToRoleAsync(BroadcastNotificationRequest request, CancellationToken cancellationToken = default);
    Task SendNotificationAsync(Guid userId, NotificationType type, string title, string message, CancellationToken cancellationToken = default);
}
