using DistributionSystem.Application.DTOs.Common;
using DistributionSystem.Application.DTOs.Notification;
using DistributionSystem.Application.Exceptions;
using DistributionSystem.Application.Services.Interfaces;
using DistributionSystem.Domain.Entities;
using DistributionSystem.Domain.Enums;
using DistributionSystem.Infrastructure.Data.Repositories.Interfaces;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace DistributionSystem.Application.Services.Implementations;

public class NotificationService : INotificationService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly INotificationPublisher _publisher;

    public NotificationService(IUnitOfWork unitOfWork, INotificationPublisher publisher)
    {
        _unitOfWork = unitOfWork;
        _publisher = publisher;
    }

    public async Task<PagedResult<NotificationDto>> GetByUserAsync(Guid userId, int page, int pageSize, bool unreadOnly = false, CancellationToken cancellationToken = default)
    {
        var query = _unitOfWork.Repository<Notification>().Query()
            .Where(n => n.UserId == userId);

        if (unreadOnly)
            query = query.Where(n => !n.IsRead);

        var total = await query.CountAsync(cancellationToken);
        var items = await query.OrderByDescending(n => n.CreatedAt)
            .ThenByDescending(n => n.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<NotificationDto>
        {
            Items = items.Select(MapToDto),
            TotalCount = total,
            Page = page,
            PageSize = pageSize
        };
    }

    public async Task<int> GetUnreadCountAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        return await _unitOfWork.Repository<Notification>().Query()
            .CountAsync(n => n.UserId == userId && !n.IsRead, cancellationToken);
    }

    public async Task MarkAsReadAsync(Guid notificationId, Guid userId, CancellationToken cancellationToken = default)
    {
        var notification = await _unitOfWork.Repository<Notification>().Query()
            .FirstOrDefaultAsync(n => n.Id == notificationId && n.UserId == userId, cancellationToken)
            ?? throw new NotFoundException("Notification", notificationId);

        notification.IsRead = true;
        notification.ReadAt = DateTime.UtcNow;
        _unitOfWork.Repository<Notification>().Update(notification);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task MarkAllAsReadAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var unread = await _unitOfWork.Repository<Notification>().Query()
            .Where(n => n.UserId == userId && !n.IsRead)
            .ToListAsync(cancellationToken);

        foreach (var n in unread)
        {
            n.IsRead = true;
            n.ReadAt = DateTime.UtcNow;
            _unitOfWork.Repository<Notification>().Update(n);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(Guid notificationId, CancellationToken cancellationToken = default)
    {
        var notification = await _unitOfWork.Repository<Notification>().GetByIdAsync(notificationId, cancellationToken)
            ?? throw new NotFoundException("Notification", notificationId);

        _unitOfWork.Repository<Notification>().Remove(notification);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task<NotificationDto> SendToUserAsync(SendNotificationRequest request, CancellationToken cancellationToken = default)
    {
        var recipientIsActive = await _unitOfWork.Repository<User>().Query()
            .AnyAsync(user => user.Id == request.UserId && user.IsActive, cancellationToken);
        if (!recipientIsActive)
            throw new NotFoundException("Active notification recipient", request.UserId);

        var type = Enum.TryParse<NotificationType>(request.Type, true, out var nt) ? nt : NotificationType.General;

        var notification = new Notification
        {
            UserId = request.UserId,
            NotificationType = type,
            Title = request.Title,
            Message = request.Message,
            Metadata = request.Metadata
        };

        await _unitOfWork.Repository<Notification>().AddAsync(notification, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        var dto = MapToDto(notification);

        // Push real-time notification via SignalR (if configured)
        await _publisher.PublishToUserAsync(request.UserId, dto, cancellationToken);

        return dto;
    }

    public async Task<IReadOnlyList<NotificationRecipientDto>> GetActiveRecipientsAsync(CancellationToken cancellationToken = default)
    {
        return await _unitOfWork.Repository<User>().Query()
            .Where(user => user.IsActive)
            .OrderBy(user => user.Role)
            .ThenBy(user => user.Username)
            .Select(user => new NotificationRecipientDto
            {
                Id = user.Id,
                Username = user.Username,
                Email = user.Email,
                Role = user.Role.ToString()
            })
            .ToListAsync(cancellationToken);
    }

    public async Task SendBroadcastAsync(BroadcastNotificationRequest request, CancellationToken cancellationToken = default)
    {
        var hasRole = !string.IsNullOrWhiteSpace(request.Role);
        var userIds = request.UserIds.Distinct().ToList();

        if (request.SendToAll)
        {
            if (hasRole || userIds.Count > 0)
                throw new ArgumentException("All-users broadcasts cannot include a role or specific recipients.");

            var activeUsers = await _unitOfWork.Repository<User>().Query()
                .Where(user => user.IsActive)
                .ToListAsync(cancellationToken);
            await SendToUsersAsync(activeUsers, request, cancellationToken);
            return;
        }

        if (hasRole)
        {
            if (userIds.Count > 0)
                throw new ArgumentException("Role broadcasts cannot include specific recipients.");

            await SendToRoleAsync(request, cancellationToken);
            return;
        }

        if (userIds.Count == 0)
            throw new ArgumentException("Select at least one active recipient, a role, or all users.");

        var recipients = await _unitOfWork.Repository<User>().Query()
            .Where(user => user.IsActive && userIds.Contains(user.Id))
            .ToListAsync(cancellationToken);
        if (recipients.Count != userIds.Count)
            throw new ArgumentException("All selected notification recipients must be active users.", nameof(request.UserIds));

        await SendToUsersAsync(recipients, request, cancellationToken);
    }

    public async Task SendToRoleAsync(BroadcastNotificationRequest request, CancellationToken cancellationToken = default)
    {
        if (!Enum.TryParse<UserRole>(request.Role, true, out var roleEnum) || !Enum.IsDefined(roleEnum))
            throw new ArgumentException("A valid notification recipient role is required.", nameof(request.Role));

        var users = await _unitOfWork.Repository<User>().Query()
            .Where(u => u.Role == roleEnum && u.IsActive)
            .ToListAsync(cancellationToken);

        await SendToUsersAsync(users, request, cancellationToken);
    }

    private async Task SendToUsersAsync(IEnumerable<User> users, BroadcastNotificationRequest request, CancellationToken cancellationToken)
    {
        var type = Enum.TryParse<NotificationType>(request.Type, true, out var nt) ? nt : NotificationType.General;

        var notifications = users.Select(user => new Notification
        {
            UserId = user.Id,
            NotificationType = type,
            Title = request.Title,
            Message = request.Message,
            Metadata = request.Metadata
        }).ToList();

        foreach (var notification in notifications)
            await _unitOfWork.Repository<Notification>().AddAsync(notification, cancellationToken);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        // Publish each persisted recipient row once, using the same DTO contract returned by GET.
        foreach (var notification in notifications)
            await _publisher.PublishToUserAsync(notification.UserId, MapToDto(notification), cancellationToken);
    }

    public async Task SendNotificationAsync(Guid userId, NotificationType type, string title, string message, CancellationToken cancellationToken = default)
    {
        var notification = new Notification
        {
            UserId = userId,
            NotificationType = type,
            Title = title,
            Message = message
        };

        await _unitOfWork.Repository<Notification>().AddAsync(notification, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        var dto = MapToDto(notification);
        await _publisher.PublishToUserAsync(userId, dto, cancellationToken);
    }

    private static NotificationDto MapToDto(Notification n) => new()
    {
        Id = n.Id,
        NotificationType = n.NotificationType.ToString(),
        Title = n.Title,
        Message = n.Message,
        IsRead = n.IsRead,
        ReadAt = n.ReadAt,
        // treat the stored UTC time as offset zero
        CreatedAt = new DateTimeOffset(DateTime.SpecifyKind(n.CreatedAt, DateTimeKind.Utc)),
        Metadata = ParseMetadata(n.Metadata)
    };

    private static JsonElement? ParseMetadata(string? metadata)
    {
        if (string.IsNullOrWhiteSpace(metadata))
            return null;

        using var document = JsonDocument.Parse(metadata);
        return document.RootElement.Clone();
    }
}
