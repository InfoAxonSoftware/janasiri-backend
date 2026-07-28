using DistributionSystem.Application.DTOs.Common;
using DistributionSystem.Application.DTOs.Notification;
using DistributionSystem.Application.Exceptions;
using DistributionSystem.Application.Services.Interfaces;
using DistributionSystem.Domain.Entities;
using DistributionSystem.Domain.Enums;
using DistributionSystem.Infrastructure.Data.Repositories.Interfaces;
using Microsoft.EntityFrameworkCore;

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

    public async Task MarkAsReadAsync(Guid notificationId, CancellationToken cancellationToken = default)
    {
        var notification = await _unitOfWork.Repository<Notification>().GetByIdAsync(notificationId, cancellationToken)
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

    public async Task SendToRoleAsync(BroadcastNotificationRequest request, CancellationToken cancellationToken = default)
    {
        var roleEnum = Enum.Parse<UserRole>(request.Role, true);
        var type = Enum.TryParse<NotificationType>(request.Type, true, out var nt) ? nt : NotificationType.General;

        var users = await _unitOfWork.Repository<User>().Query()
            .Where(u => u.Role == roleEnum && u.IsActive)
            .ToListAsync(cancellationToken);

        foreach (var user in users)
        {
            await _unitOfWork.Repository<Notification>().AddAsync(new Notification
            {
                UserId = user.Id,
                NotificationType = type,
                Title = request.Title,
                Message = request.Message,
                Metadata = request.Metadata
            }, cancellationToken);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        // Push to SignalR group for this role
        await _publisher.PublishToRoleAsync(request.Role, new { request.Title, request.Message, request.Type }, cancellationToken);
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
        Metadata = n.Metadata
    };
}
