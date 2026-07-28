using DistributionSystem.Application.DTOs.Common;
using DistributionSystem.Application.DTOs.Support;
using DistributionSystem.Application.Exceptions;
using DistributionSystem.Application.Services.Interfaces;
using DistributionSystem.Domain.Entities;
using DistributionSystem.Domain.Enums;
using DistributionSystem.Infrastructure.Data.Repositories.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace DistributionSystem.Application.Services.Implementations;

public class SupportService : ISupportService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly INotificationService _notificationService;
    private readonly ISupportChatPublisher _chatPublisher;
    private readonly IEmailService _emailService;

    public SupportService(IUnitOfWork unitOfWork, INotificationService notificationService, ISupportChatPublisher chatPublisher, IEmailService emailService)
    {
        _unitOfWork = unitOfWork;
        _notificationService = notificationService;
        _chatPublisher = chatPublisher;
        _emailService = emailService;
    }

    public async Task<ComplaintDto> CustomerCreateComplaintAsync(Guid userId, CreateComplaintRequest request, CancellationToken ct)
    {
        var customer = await _unitOfWork.Repository<CustomerProfile>().Query()
            .Include(c => c.User)
            .FirstOrDefaultAsync(c => c.UserId == userId, ct)
            ?? throw new NotFoundException("Customer", userId);

        var complaint = await CreateComplaintAsync(userId, UserRole.Customer.ToString(), request, customer.Id, ct);
        await NotifyAdminsAsync(
            $"Customer support request: {complaint.Subject}",
            complaint.Description,
            ct,
            $"New Customer Support Request - {complaint.Subject}",
            $"<p>A customer opened a support ticket:</p><p><strong>{complaint.Subject}</strong></p><p>{System.Net.WebUtility.HtmlEncode(complaint.Description)}</p>");

        return await MapComplaintAsync(complaint, ct);
    }

    public async Task<ComplaintDto> RepCreateComplaintAsync(Guid userId, CreateComplaintRequest request, CancellationToken ct)
    {
        _ = await _unitOfWork.Repository<SalesRepProfile>().Query()
            .FirstOrDefaultAsync(r => r.UserId == userId, ct)
            ?? throw new NotFoundException("SalesRep", userId);

        var complaint = await CreateComplaintAsync(userId, UserRole.SalesRep.ToString(), request, null, ct);
        await NotifyAdminsAsync(
            $"Rep support request: {complaint.Subject}",
            complaint.Description,
            ct,
            $"New Rep Support Request - {complaint.Subject}",
            $"<p>A sales rep opened a support ticket:</p><p><strong>{complaint.Subject}</strong></p><p>{System.Net.WebUtility.HtmlEncode(complaint.Description)}</p>");

        return await MapComplaintAsync(complaint, ct);
    }

    public async Task<ComplaintDto> CoordinatorCreateComplaintAsync(Guid userId, CreateComplaintRequest request, CancellationToken ct)
    {
        _ = await _unitOfWork.Repository<CoordinatorProfile>().Query()
            .FirstOrDefaultAsync(r => r.UserId == userId, ct)
            ?? throw new NotFoundException("SalesCoordinator", userId);

        var complaint = await CreateComplaintAsync(userId, UserRole.SalesCoordinator.ToString(), request, null, ct);
        await NotifyAdminsAsync(
            $"Coordinator support request: {complaint.Subject}",
            complaint.Description,
            ct,
            $"New Coordinator Support Request - {complaint.Subject}",
            $"<p>A coordinator opened a support ticket:</p><p><strong>{complaint.Subject}</strong></p><p>{System.Net.WebUtility.HtmlEncode(complaint.Description)}</p>");

        return await MapComplaintAsync(complaint, ct);
    }

    public async Task<List<ComplaintDto>> CustomerGetComplaintsAsync(Guid userId, CancellationToken ct)
    {
        var customer = await _unitOfWork.Repository<CustomerProfile>().Query()
            .FirstOrDefaultAsync(c => c.UserId == userId, ct)
            ?? throw new NotFoundException("Customer", userId);

        var items = await _unitOfWork.Repository<Complaint>().Query()
            .Where(c => c.CustomerId == customer.Id)
            .OrderByDescending(c => c.CreatedAt)
            .ToListAsync(ct);

        var list = new List<ComplaintDto>(items.Count);
        foreach (var item in items) list.Add(await MapComplaintAsync(item, ct));
        return list;
    }

    public async Task<List<ComplaintDto>> RepGetComplaintsAsync(Guid userId, CancellationToken ct)
    {
        var uid = userId.ToString();
        var items = await _unitOfWork.Repository<Complaint>().Query()
            .Where(c => c.CreatedBy == uid)
            .OrderByDescending(c => c.CreatedAt)
            .ToListAsync(ct);

        var list = new List<ComplaintDto>(items.Count);
        foreach (var item in items) list.Add(await MapComplaintAsync(item, ct));
        return list;
    }

    public async Task<List<ComplaintDto>> CoordinatorGetComplaintsAsync(Guid userId, CancellationToken ct)
    {
        var uid = userId.ToString();
        var items = await _unitOfWork.Repository<Complaint>().Query()
            .Where(c => c.CreatedBy == uid)
            .OrderByDescending(c => c.CreatedAt)
            .ToListAsync(ct);

        var list = new List<ComplaintDto>(items.Count);
        foreach (var item in items) list.Add(await MapComplaintAsync(item, ct));
        return list;
    }

    public async Task<PagedResult<ComplaintDto>> AdminGetComplaintsAsync(int page, int pageSize, string? status, CancellationToken ct)
    {
        var query = _unitOfWork.Repository<Complaint>().Query();
        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<ComplaintStatus>(status, true, out var st))
        {
            query = query.Where(c => c.Status == st);
        }

        var total = await query.CountAsync(ct);
        var items = await query.OrderByDescending(c => c.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        var mapped = new List<ComplaintDto>(items.Count);
        foreach (var item in items) mapped.Add(await MapComplaintAsync(item, ct));

        return new PagedResult<ComplaintDto>
        {
            Items = mapped,
            TotalCount = total,
            Page = page,
            PageSize = pageSize
        };
    }

    public async Task EnsureComplaintAccessAsync(Guid userId, string role, Guid complaintId, CancellationToken ct)
    {
        await EnsureAccessAsync(userId, role, complaintId, ct);
    }

    public async Task<ComplaintDto> AdminUpdateStatusAsync(Guid adminUserId, Guid complaintId, string status, CancellationToken ct)
    {
        var complaint = await _unitOfWork.Repository<Complaint>().GetByIdAsync(complaintId, ct)
            ?? throw new NotFoundException("Complaint", complaintId);

        if (!Enum.TryParse<ComplaintStatus>(status, true, out var parsed))
            throw new BusinessException("Invalid complaint status");

        complaint.Status = parsed;
        if (parsed == ComplaintStatus.Resolved || parsed == ComplaintStatus.Closed)
        {
            complaint.ResolvedBy = adminUserId;
            complaint.ResolvedAt = DateTime.UtcNow;
        }

        _unitOfWork.Repository<Complaint>().Update(complaint);
        await _unitOfWork.SaveChangesAsync(ct);

        await NotifyComplaintOwnerAsync(
            complaint,
            NotificationType.SupportResolution,
            $"Support ticket updated: {complaint.Subject}",
            $"Status changed to {parsed}",
            $"Support Ticket Status Updated - {complaint.Subject}",
            $"<p>Your support ticket <strong>{complaint.Subject}</strong> is now <strong>{parsed}</strong>.</p>",
            ct);

        await AddSystemMessageAsync(adminUserId, complaint.Id, $"Status changed to {parsed}", ct);

        await _chatPublisher.PublishStatusChangedAsync(complaint.Id, new
        {
            ComplaintId = complaint.Id,
            Status = parsed.ToString(),
            UpdatedAt = DateTime.UtcNow
        }, ct);

        return await MapComplaintAsync(complaint, ct);
    }

    public async Task<List<ComplaintMessageDto>> GetMessagesAsync(Guid userId, string role, Guid complaintId, CancellationToken ct)
    {
        await EnsureAccessAsync(userId, role, complaintId, ct);

        var messages = await _unitOfWork.Repository<ComplaintMessage>().Query()
            .Where(m => m.ComplaintId == complaintId)
            .OrderBy(m => m.CreatedAt)
            .ToListAsync(ct);

        var dtos = new List<ComplaintMessageDto>(messages.Count);
        foreach (var m in messages)
        {
            dtos.Add(new ComplaintMessageDto
            {
                Id = m.Id,
                ComplaintId = m.ComplaintId,
                SenderUserId = m.SenderUserId,
                SenderRole = m.SenderRole,
                SenderName = await ResolveSenderName(m.SenderRole, m.SenderUserId, ct),
                Message = m.Message,
                IsSystemMessage = m.IsSystemMessage,
                CreatedAt = m.CreatedAt
            });
        }

        return dtos;
    }

    public async Task<ComplaintMessageDto> SendMessageAsync(Guid userId, string role, Guid complaintId, SendComplaintMessageRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Message)) throw new BusinessException("Message is required");

        var complaint = await EnsureAccessAsync(userId, role, complaintId, ct);
        if (complaint.Status == ComplaintStatus.Resolved || complaint.Status == ComplaintStatus.Closed)
        {
            throw new BusinessException("Ticket is closed. Reopen it to continue chatting.");
        }

        var normalizedRole = role.Trim();

        var msg = new ComplaintMessage
        {
            ComplaintId = complaintId,
            SenderUserId = userId,
            SenderRole = normalizedRole,
            Message = request.Message.Trim(),
            CreatedBy = userId.ToString()
        };

        await _unitOfWork.Repository<ComplaintMessage>().AddAsync(msg, ct);

        var statusChanged = false;
        if (complaint.Status == ComplaintStatus.Open)
        {
            complaint.Status = ComplaintStatus.InProgress;
            _unitOfWork.Repository<Complaint>().Update(complaint);
            statusChanged = true;
        }

        await _unitOfWork.SaveChangesAsync(ct);

        var senderName = await ResolveSenderName(normalizedRole, userId, ct);

        var dto = new ComplaintMessageDto
        {
            Id = msg.Id,
            ComplaintId = msg.ComplaintId,
            SenderUserId = msg.SenderUserId,
            SenderRole = msg.SenderRole,
            SenderName = senderName,
            Message = msg.Message,
            IsSystemMessage = msg.IsSystemMessage,
            CreatedAt = msg.CreatedAt
        };

        await _chatPublisher.PublishMessageAsync(complaintId, dto, ct);

        if (statusChanged)
        {
            await _chatPublisher.PublishStatusChangedAsync(complaint.Id, new
            {
                ComplaintId = complaint.Id,
                Status = complaint.Status.ToString(),
                UpdatedAt = DateTime.UtcNow
            }, ct);
        }

        if (normalizedRole.Equals(UserRole.Admin.ToString(), StringComparison.OrdinalIgnoreCase))
        {
            await NotifyComplaintOwnerAsync(
                complaint,
                NotificationType.ComplaintUpdate,
                $"New support message: {complaint.Subject}",
                "Admin replied to your support ticket",
                $"Admin Reply - {complaint.Subject}",
                $"<p>Admin replied to your ticket <strong>{complaint.Subject}</strong>:</p><p>{System.Net.WebUtility.HtmlEncode(msg.Message)}</p>",
                ct);
        }
        else
        {
            await NotifyAdminsAsync(
                $"New support message: {complaint.Subject}",
                $"{senderName}: {msg.Message}",
                ct,
                $"Support Message - {complaint.Subject}",
                $"<p><strong>{senderName}</strong> sent a message on ticket <strong>{complaint.Subject}</strong>:</p><p>{System.Net.WebUtility.HtmlEncode(msg.Message)}</p>");
        }

        return dto;
    }

    private async Task<Complaint> CreateComplaintAsync(Guid userId, string role, CreateComplaintRequest request, Guid? customerId, CancellationToken ct)
    {
        if (!Enum.TryParse<ComplaintPriority>(request.Priority, true, out var priority))
            priority = ComplaintPriority.Medium;

        var ticketType = NormalizeTicketType(request.TicketType);

        var complaint = new Complaint
        {
            CustomerId = customerId,
            OrderId = request.OrderId,
            Subject = BuildStoredSubject(request.Subject.Trim(), ticketType),
            Description = request.Description.Trim(),
            ContactName = request.ContactName,
            ContactEmail = request.ContactEmail,
            ContactPhone = request.ContactPhone,
            ContactPosition = request.ContactPosition,
            Priority = priority,
            Status = ComplaintStatus.Open,
            CreatedBy = userId.ToString(),
            CreatedByRole = role
        };

        await _unitOfWork.Repository<Complaint>().AddAsync(complaint, ct);
        await _unitOfWork.SaveChangesAsync(ct);

        await AddSystemMessageAsync(userId, complaint.Id, "Ticket created", ct);
        return complaint;
    }

    private async Task AddSystemMessageAsync(Guid actorUserId, Guid complaintId, string text, CancellationToken ct)
    {
        await _unitOfWork.Repository<ComplaintMessage>().AddAsync(new ComplaintMessage
        {
            ComplaintId = complaintId,
            SenderUserId = actorUserId,
            SenderRole = "System",
            Message = text,
            IsSystemMessage = true,
            CreatedBy = actorUserId.ToString()
        }, ct);
        await _unitOfWork.SaveChangesAsync(ct);
    }

    private async Task NotifyAdminsAsync(string title, string message, CancellationToken ct, string? emailSubject = null, string? emailHtml = null)
    {
        var admins = await GetActiveAdminsAsync(ct);

        foreach (var admin in admins)
        {
            await _notificationService.SendNotificationAsync(admin.Id, NotificationType.ComplaintUpdate, title, message, ct);
            if (!string.IsNullOrWhiteSpace(admin.Email) && !string.IsNullOrWhiteSpace(emailSubject) && !string.IsNullOrWhiteSpace(emailHtml))
            {
                await _emailService.SendEmailAsync(admin.Email, emailSubject, emailHtml, ct);
            }
        }
    }

    private async Task<List<User>> GetActiveAdminsAsync(CancellationToken ct)
    {
        return await _unitOfWork.Repository<User>().Query()
            .Where(u => u.Role == UserRole.Admin && u.IsActive)
            .ToListAsync(ct);
    }

    private async Task NotifyComplaintOwnerAsync(
        Complaint complaint,
        NotificationType notificationType,
        string notificationTitle,
        string notificationMessage,
        string emailSubject,
        string emailHtml,
        CancellationToken ct)
    {
        var owner = await ResolveComplaintOwnerAsync(complaint, ct);
        if (owner == null) return;

        await _notificationService.SendNotificationAsync(owner.Value.UserId, notificationType, notificationTitle, notificationMessage, ct);

        if (!string.IsNullOrWhiteSpace(owner.Value.Email))
        {
            await _emailService.SendEmailAsync(owner.Value.Email!, emailSubject, emailHtml, ct);
        }
    }

    private async Task<(Guid UserId, string? Email)?> ResolveComplaintOwnerAsync(Complaint complaint, CancellationToken ct)
    {
        if (complaint.CreatedByRole.Equals(UserRole.Customer.ToString(), StringComparison.OrdinalIgnoreCase) && complaint.CustomerId.HasValue)
        {
            var customer = await _unitOfWork.Repository<CustomerProfile>().Query()
                .Include(c => c.User)
                .FirstOrDefaultAsync(c => c.Id == complaint.CustomerId.Value, ct);

            if (customer != null)
                return (customer.UserId, customer.User?.Email);
        }

        if (Guid.TryParse(complaint.CreatedBy, out var creatorUserId))
        {
            var user = await _unitOfWork.Repository<User>().GetByIdAsync(creatorUserId, ct);
            if (user != null)
                return (user.Id, user.Email);
        }

        return null;
    }

    private async Task<Complaint> EnsureAccessAsync(Guid userId, string role, Guid complaintId, CancellationToken ct)
    {
        var complaint = await _unitOfWork.Repository<Complaint>().GetByIdAsync(complaintId, ct)
            ?? throw new NotFoundException("Complaint", complaintId);

        if (role.Equals(UserRole.Admin.ToString(), StringComparison.OrdinalIgnoreCase))
            return complaint;

        if (role.Equals(UserRole.Customer.ToString(), StringComparison.OrdinalIgnoreCase))
        {
            var customer = await _unitOfWork.Repository<CustomerProfile>().Query().FirstOrDefaultAsync(c => c.UserId == userId, ct)
                ?? throw new NotFoundException("Customer", userId);
            if (complaint.CustomerId == customer.Id) return complaint;
        }
        else if (string.Equals(complaint.CreatedBy, userId.ToString(), StringComparison.OrdinalIgnoreCase))
        {
            return complaint;
        }

        throw new BusinessException("You do not have access to this support ticket");
    }

    private async Task<ComplaintDto> MapComplaintAsync(Complaint complaint, CancellationToken ct)
    {
        var creatorId = Guid.TryParse(complaint.CreatedBy, out var parsed) ? parsed : Guid.Empty;
        var creatorName = creatorId != Guid.Empty
            ? await ResolveSenderName(complaint.CreatedByRole, creatorId, ct)
            : "User";
        var (ticketType, subject) = ParseStoredSubject(complaint.Subject);

        string customerName = "-";
        if (complaint.CustomerId.HasValue)
        {
            var customer = await _unitOfWork.Repository<CustomerProfile>().GetByIdAsync(complaint.CustomerId.Value, ct);
            if (customer != null) customerName = customer.ShopName;
        }

        return new ComplaintDto
        {
            Id = complaint.Id,
            CustomerId = complaint.CustomerId,
            CustomerName = customerName,
            CreatedByUserId = creatorId,
            CreatedByRole = complaint.CreatedByRole,
            CreatedByName = creatorName,
            AssignedTo = complaint.AssignedTo,
            OrderId = complaint.OrderId,
            Subject = subject,
            TicketType = ticketType,
            Description = complaint.Description,
            ContactName = complaint.ContactName,
            ContactEmail = complaint.ContactEmail,
            ContactPhone = complaint.ContactPhone,
            ContactPosition = complaint.ContactPosition,
            Priority = complaint.Priority.ToString(),
            Status = complaint.Status.ToString(),
            CreatedAt = complaint.CreatedAt,
            ResolvedAt = complaint.ResolvedAt
        };
    }

    private static string NormalizeTicketType(string? ticketType)
    {
        if (string.Equals(ticketType, "Support", StringComparison.OrdinalIgnoreCase))
        {
            return "Support";
        }

        return "Complaint";
    }

    private static string BuildStoredSubject(string subject, string ticketType)
    {
        if (subject.StartsWith("[Support]", StringComparison.OrdinalIgnoreCase) ||
            subject.StartsWith("[Complaint]", StringComparison.OrdinalIgnoreCase))
        {
            return subject;
        }

        return $"[{ticketType}] {subject}";
    }

    private static (string TicketType, string Subject) ParseStoredSubject(string subject)
    {
        if (subject.StartsWith("[Support]", StringComparison.OrdinalIgnoreCase))
        {
            return ("Support", subject[9..].TrimStart());
        }

        if (subject.StartsWith("[Complaint]", StringComparison.OrdinalIgnoreCase))
        {
            return ("Complaint", subject[11..].TrimStart());
        }

        return ("Complaint", subject);
    }

    private async Task<string> ResolveSenderName(string role, Guid userId, CancellationToken ct = default)
    {
        if (role.Equals(UserRole.Customer.ToString(), StringComparison.OrdinalIgnoreCase))
        {
            var customer = await _unitOfWork.Repository<CustomerProfile>().Query()
                .FirstOrDefaultAsync(c => c.UserId == userId, ct);
            return customer?.ShopName ?? "Customer";
        }

        if (role.Equals(UserRole.SalesRep.ToString(), StringComparison.OrdinalIgnoreCase))
        {
            var rep = await _unitOfWork.Repository<SalesRepProfile>().Query()
                .FirstOrDefaultAsync(r => r.UserId == userId, ct);
            return rep?.FullName ?? "Sales Rep";
        }

        if (role.Equals(UserRole.SalesCoordinator.ToString(), StringComparison.OrdinalIgnoreCase))
        {
            var coord = await _unitOfWork.Repository<CoordinatorProfile>().Query()
                .FirstOrDefaultAsync(c => c.UserId == userId, ct);
            return coord?.FullName ?? "Coordinator";
        }

        var user = await _unitOfWork.Repository<User>().GetByIdAsync(userId, ct);
        return user?.Username ?? "Admin";
    }
}
