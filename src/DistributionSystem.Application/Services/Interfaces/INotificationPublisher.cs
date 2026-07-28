using DistributionSystem.Application.DTOs.Notification;
using DistributionSystem.Application.DTOs.RepPayments;

namespace DistributionSystem.Application.Services.Interfaces;

public interface INotificationPublisher
{
    Task PublishToUserAsync(Guid userId, NotificationDto notification, CancellationToken cancellationToken = default);
    Task PublishToRoleAsync(string role, object payload, CancellationToken cancellationToken = default);

    // Narrowly-targeted realtime event for Payment Report list/detail refreshes.
    // Sent only to the given users' personal SignalR groups (never a broad broadcast).
    Task PublishPaymentReportEventAsync(PaymentReportEventDto evt, IEnumerable<Guid> targetUserIds, CancellationToken cancellationToken = default);
}
