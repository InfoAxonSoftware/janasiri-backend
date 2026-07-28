using System.Text.Json;
using DistributionSystem.Application.DTOs.Common;
using DistributionSystem.Application.DTOs.Notification;
using DistributionSystem.Application.DTOs.RepPayments;
using DistributionSystem.Application.Exceptions;
using DistributionSystem.Application.Services.Interfaces;
using DistributionSystem.Domain.Entities;
using DistributionSystem.Domain.Enums;
using DistributionSystem.Infrastructure.Data.Repositories.Interfaces;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DistributionSystem.Application.Services.Implementations;

public class RepPaymentService : IRepPaymentService
{
    private readonly IUnitOfWork _uow;
    private readonly IWebHostEnvironment _env;
    private readonly INotificationService _notifications;
    private readonly INotificationPublisher _publisher;
    private readonly ILogger<RepPaymentService> _logger;
    private readonly IFileStorageService _fileStorage;

    private static readonly string[] AllowedImageTypes =
        ["image/jpeg", "image/png", "image/webp", "image/gif"];
    private static readonly string[] AllowedImageExtensions =
        [".jpg", ".jpeg", ".png", ".webp", ".gif"];
    private const long MaxImageSizeBytes = 10 * 1024 * 1024;

    public RepPaymentService(
        IUnitOfWork uow,
        IWebHostEnvironment env,
        INotificationService notifications,
        INotificationPublisher publisher,
        ILogger<RepPaymentService> logger,
        IFileStorageService fileStorage)
    {
        _uow = uow;
        _env = env;
        _notifications = notifications;
        _publisher = publisher;
        _logger = logger;
        _fileStorage = fileStorage;
    }

    // ── Rep ──────────────────────────────────────────────────────────────────

    public async Task<RepPaymentDto> CreateAsync(Guid repUserId, CreateRepPaymentDto dto, IFormFile? image, CancellationToken ct = default)
    {
        var rep = await _uow.Repository<SalesRepProfile>().Query()
            .FirstOrDefaultAsync(r => r.UserId == repUserId, ct)
            ?? throw new NotFoundException("Sales rep profile", repUserId);

        var referenceNumber = dto.ReferenceNumber?.Trim();
        if (string.IsNullOrWhiteSpace(referenceNumber))
            throw new ArgumentException("Reference number is required.");

        if (referenceNumber.Length > 100)
            throw new ArgumentException("Reference number must not exceed 100 characters.");

        string? imageUrl = null;

        if (image is { Length: > 0 })
        {
            var saved = await _fileStorage.SaveAsync(
                image, "rep-payments", FileAccessCategory.Private,
                AllowedImageExtensions, AllowedImageTypes, MaxImageSizeBytes, ct);
            imageUrl = saved.StorageKey;
        }

        var payment = new RepPayment
        {
            RepId = rep.Id,
            CustomerName = dto.CustomerName.Trim(),
            ReferenceNumber = referenceNumber,
            Amount = dto.Amount,
            ImageUrl = imageUrl,
            Status = RepPaymentStatus.AwaitingConfirmation,
            CreatedBy = rep.FullName,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };

        // Save the report first; notifications/realtime are only published once this succeeds.
        await _uow.Repository<RepPayment>().AddAsync(payment, ct);
        await _uow.SaveChangesAsync(ct);

        var coordUserIds = await GetAssignedCoordinatorUserIdsAsync(rep.Id, ct);

        // Await notification persistence and realtime publication inside the
        // request scope. Fire-and-forget Task.Run caused intermittent failures
        // when the scoped DbContext/services were disposed after the response.
        await NotifySubmissionAsync(payment, rep, coordUserIds, ct);

        return MapToDto(payment, rep.FullName, null);
    }

    private async Task NotifySubmissionAsync(
        RepPayment payment,
        SalesRepProfile rep,
        List<Guid> coordUserIds,
        CancellationToken ct)
    {
        try
        {
            var adminUserIds = await _uow.Repository<User>().Query()
                .Where(u => u.IsActive && (u.Role == UserRole.Admin || u.Role == UserRole.SuperAdmin))
                .Select(u => u.Id)
                .ToListAsync(ct);

            if (coordUserIds.Count == 0)
            {
                _logger.LogWarning(
                    "Payment report {ReportId} submitted by rep {RepId} has no assigned coordinator; notifying admins only.",
                    payment.Id, rep.Id);
            }

            var metadata = JsonSerializer.Serialize(new
            {
                reportId = payment.Id,
                repName = rep.FullName,
                customerName = payment.CustomerName,
                amount = payment.Amount,
                submittedAt = payment.CreatedAt,
            });

            var title = "New Payment Report Submitted";
            var message = $"{rep.FullName} submitted a payment report for {payment.CustomerName} — LKR {payment.Amount:N2}";

            foreach (var adminUserId in adminUserIds)
            {
                await SendOnceAsync(adminUserId, NotificationType.PaymentReportSubmitted, payment.Id, title, message, metadata, ct);
            }

            foreach (var coordUserId in coordUserIds)
            {
                await SendOnceAsync(coordUserId, NotificationType.PaymentReportSubmitted, payment.Id, title, message, metadata, ct);
            }

            var targets = adminUserIds.Concat(coordUserIds).Distinct().ToList();
            if (targets.Count > 0)
            {
                await _publisher.PublishPaymentReportEventAsync(new PaymentReportEventDto
                {
                    EventType = "paymentReportCreated",
                    ReportId = payment.Id,
                    SalesRepUserId = rep.UserId,
                    CoordinatorUserId = coordUserIds.FirstOrDefault(),
                    NewStatus = payment.Status.ToString(),
                    ActionUrl = $"/admin/payment-reports/{payment.Id}",
                    OccurredAt = DateTime.UtcNow,
                }, targets, ct);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send payment report submission notifications for report {ReportId}.", payment.Id);
        }
    }

    // Skip creating a duplicate notification for the same user/report/type (defends against retries).
    private async Task SendOnceAsync(
        Guid userId,
        NotificationType type,
        Guid reportId,
        string title,
        string message,
        string metadata,
        CancellationToken ct)
    {
        var exists = await _uow.Repository<Notification>().Query()
            .AnyAsync(
                n => n.UserId == userId &&
                     n.NotificationType == type &&
                     n.Metadata != null &&
                     n.Metadata.Contains(reportId.ToString()),
                ct);
        if (exists) return;

        await _notifications.SendToUserAsync(new SendNotificationRequest
        {
            UserId = userId,
            Title = title,
            Message = message,
            Type = type.ToString(),
            Metadata = metadata,
        }, ct);
    }

    public async Task<PagedResult<RepPaymentDto>> GetRepPaymentsAsync(
        Guid repUserId,
        RepPaymentQueryDto filter,
        bool trash = false,
        CancellationToken ct = default)
    {
        var rep = await _uow.Repository<SalesRepProfile>().Query()
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.UserId == repUserId, ct)
            ?? throw new NotFoundException("Sales rep profile", repUserId);

        var query = _uow.Repository<RepPayment>().Query()
            .AsNoTracking()
            .Include(p => p.Rep)
            .Where(p => p.RepId == rep.Id && p.IsDeletedBySalesRep == trash);

        return await ExecutePagedQueryAsync(query, filter, ct);
    }

    // Backward-compatible list overload for existing tests/callers.
    public async Task<List<RepPaymentDto>> GetRepPaymentsAsync(
        Guid repUserId,
        bool trash = false,
        CancellationToken ct = default)
    {
        var filter = new RepPaymentQueryDto
        {
            Page = 1,
            PageSize = 500,
        };

        var firstPage = await GetRepPaymentsAsync(repUserId, filter, trash, ct);
        var items = firstPage.Items.ToList();

        while (items.Count < firstPage.TotalCount)
        {
            filter.Page++;
            var nextPage = await GetRepPaymentsAsync(repUserId, filter, trash, ct);
            if (!nextPage.Items.Any()) break;
            items.AddRange(nextPage.Items);
        }

        return items;
    }

    public async Task<RepPaymentDto> GetForRepByIdAsync(Guid id, Guid repUserId, CancellationToken ct = default)
    {
        var rep = await _uow.Repository<SalesRepProfile>().Query()
            .FirstOrDefaultAsync(r => r.UserId == repUserId, ct)
            ?? throw new NotFoundException("Sales rep profile", repUserId);

        var payment = await _uow.Repository<RepPayment>().Query()
            .Include(p => p.Rep)
            .FirstOrDefaultAsync(p => p.Id == id && p.RepId == rep.Id, ct)
            ?? throw new NotFoundException("Payment report", id);

        return MapToDto(payment, payment.Rep?.FullName ?? string.Empty, await GetCoordinatorNameAsync(payment.RepId, ct));
    }

    /// <summary>Resolves the evidence storage key for a payment already confirmed to be in scope by the caller (role-appropriate GetById* method). Returns null if the request has no evidence image.</summary>
    public async Task<string?> GetEvidenceStorageKeyAsync(Guid id, CancellationToken ct = default)
    {
        var payment = await _uow.Repository<RepPayment>().Query()
            .FirstOrDefaultAsync(p => p.Id == id, ct)
            ?? throw new NotFoundException("Payment report", id);

        return payment.ImageUrl;
    }

    public async Task DeleteRepAsync(Guid id, Guid repUserId, CancellationToken ct = default)
    {
        var rep = await _uow.Repository<SalesRepProfile>().Query()
            .FirstOrDefaultAsync(r => r.UserId == repUserId, ct)
            ?? throw new NotFoundException("Sales rep profile", repUserId);

        var payment = await _uow.Repository<RepPayment>().Query()
            .FirstOrDefaultAsync(p => p.Id == id && p.RepId == rep.Id, ct)
            ?? throw new NotFoundException("Payment report", id);

        DeleteImageFile(payment.ImageUrl);
        _uow.Repository<RepPayment>().Remove(payment);
        await _uow.SaveChangesAsync(ct);
    }

    public async Task RepTrashAsync(Guid id, Guid repUserId, CancellationToken ct = default)
    {
        var rep = await _uow.Repository<SalesRepProfile>().Query()
            .FirstOrDefaultAsync(r => r.UserId == repUserId, ct)
            ?? throw new NotFoundException("Sales rep profile", repUserId);

        var payment = await _uow.Repository<RepPayment>().Query()
            .FirstOrDefaultAsync(p => p.Id == id && p.RepId == rep.Id, ct)
            ?? throw new NotFoundException("Payment report", id);

        payment.IsDeletedBySalesRep = true;
        payment.SalesRepDeletedAt = DateTime.UtcNow;
        await _uow.SaveChangesAsync(ct);
    }

    public async Task RepRestoreAsync(Guid id, Guid repUserId, CancellationToken ct = default)
    {
        var rep = await _uow.Repository<SalesRepProfile>().Query()
            .FirstOrDefaultAsync(r => r.UserId == repUserId, ct)
            ?? throw new NotFoundException("Sales rep profile", repUserId);

        var payment = await _uow.Repository<RepPayment>().Query()
            .FirstOrDefaultAsync(p => p.Id == id && p.RepId == rep.Id, ct)
            ?? throw new NotFoundException("Payment report", id);

        payment.IsDeletedBySalesRep = false;
        payment.SalesRepDeletedAt = null;
        await _uow.SaveChangesAsync(ct);
    }

    // ── Admin ─────────────────────────────────────────────────────────────────

    public async Task<PagedResult<RepPaymentDto>> GetAllAsync(
        RepPaymentQueryDto filter,
        bool trash = false,
        CancellationToken ct = default)
    {
        var query = _uow.Repository<RepPayment>().Query()
            .AsNoTracking()
            .Include(p => p.Rep)
            .Where(p => p.IsDeletedByAdmin == trash);

        return await ExecutePagedQueryAsync(query, filter, ct);
    }

    // Backward-compatible list overload for existing tests/callers.
    public async Task<List<RepPaymentDto>> GetAllAsync(
        string? status = null,
        string? repId = null,
        CancellationToken ct = default)
    {
        Guid? parsedRepId = null;
        if (!string.IsNullOrWhiteSpace(repId) && Guid.TryParse(repId, out var value))
        {
            parsedRepId = value;
        }

        var filter = new RepPaymentQueryDto
        {
            Page = 1,
            PageSize = 500,
            Status = status,
            RepId = parsedRepId,
        };

        var firstPage = await GetAllAsync(filter, trash: false, ct);
        var items = firstPage.Items.ToList();

        while (items.Count < firstPage.TotalCount)
        {
            filter.Page++;
            var nextPage = await GetAllAsync(filter, trash: false, ct);
            if (!nextPage.Items.Any()) break;
            items.AddRange(nextPage.Items);
        }

        return items;
    }

    public async Task<List<RepPaymentRepOptionDto>> GetAdminRepOptionsAsync(
        CancellationToken ct = default)
    {
        return await _uow.Repository<RepPayment>().Query()
            .AsNoTracking()
            .Where(p => !p.IsDeletedByAdmin)
            .Select(p => new RepPaymentRepOptionDto
            {
                Id = p.RepId,
                Name = p.Rep.FullName,
            })
            .Distinct()
            .OrderBy(option => option.Name)
            .ToListAsync(ct);
    }

    public async Task<RepPaymentDto> GetByIdForAdminAsync(Guid id, CancellationToken ct = default)
    {
        var payment = await _uow.Repository<RepPayment>().Query()
            .Include(p => p.Rep)
            .FirstOrDefaultAsync(p => p.Id == id, ct)
            ?? throw new NotFoundException("Payment report", id);

        return MapToDto(payment, payment.Rep?.FullName ?? string.Empty, await GetCoordinatorNameAsync(payment.RepId, ct));
    }

    public async Task<RepPaymentDto> UpdateStatusAsync(Guid id, UpdateRepPaymentStatusDto dto, Guid updatedByUserId, string updatedBy, CancellationToken ct = default)
    {
        var payment = await _uow.Repository<RepPayment>().Query()
            .Include(p => p.Rep)
            .FirstOrDefaultAsync(p => p.Id == id, ct)
            ?? throw new NotFoundException("Payment report", id);

        if (!Enum.TryParse<RepPaymentStatus>(dto.Status, ignoreCase: true, out var newStatus))
            throw new ArgumentException($"Invalid status '{dto.Status}'.");

        var oldStatus = payment.Status;

        payment.Status = newStatus;
        payment.AdminNotes = dto.AdminNotes;
        payment.UpdatedAt = DateTime.UtcNow;
        payment.UpdatedBy = updatedBy;

        await _uow.SaveChangesAsync(ct);

        if (oldStatus != newStatus)
        {
            var repUserId = payment.Rep?.UserId ?? Guid.Empty;
            var coordUserIds = await GetAssignedCoordinatorUserIdsAsync(payment.RepId, ct);
            Guid? coordinatorUserId = coordUserIds.Count > 0 ? coordUserIds[0] : null;

            await NotifyStatusChangeAsync(
                payment,
                repUserId,
                oldStatus,
                newStatus,
                updatedBy,
                coordinatorUserId,
                notifyCoordinator: true,
                ct: ct);
        }

        return MapToDto(payment, payment.Rep?.FullName ?? string.Empty, await GetCoordinatorNameAsync(payment.RepId, ct));
    }

    private async Task NotifyStatusChangeAsync(
        RepPayment payment,
        Guid repUserId,
        RepPaymentStatus oldStatus,
        RepPaymentStatus newStatus,
        string reviewer,
        Guid? coordinatorUserId,
        bool notifyCoordinator,
        CancellationToken ct)
    {
        if (repUserId == Guid.Empty)
        {
            _logger.LogWarning(
                "Cannot send Payment Report status notification because the Sales Rep UserId was not found. ReportId: {ReportId}",
                payment.Id);

            return;
        }

        var repActionUrl = $"/rep/payment-reports/{payment.Id}";
        var coordinatorActionUrl = $"/coordinator/payment-reports/{payment.Id}";

        var repMetadata = JsonSerializer.Serialize(new
        {
            reportId = payment.Id,
            customerName = payment.CustomerName,
            amount = payment.Amount,
            oldStatus = oldStatus.ToString(),
            newStatus = newStatus.ToString(),
            reviewer,
            updatedAt = payment.UpdatedAt,
            actionUrl = repActionUrl,
        });

        // Persistent notification for the Sales Rep who submitted the report.
        try
        {
            await _notifications.SendToUserAsync(new SendNotificationRequest
            {
                UserId = repUserId,
                Title = "Payment Report Status Changed",
                Message = $"Your payment report for {payment.CustomerName} (LKR {payment.Amount:N2}) changed from {oldStatus} to {newStatus}.",
                Type = NotificationType.PaymentReportStatusChanged.ToString(),
                Metadata = repMetadata,
            }, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to create the status-change notification for Payment Report {ReportId} and Sales Rep user {RepUserId}.",
                payment.Id,
                repUserId);
        }

        // When Admin/SuperAdmin changes the status, also notify only the
        // Coordinator currently assigned to this Sales Rep. When the
        // Coordinator changes the status themselves, notifyCoordinator is false
        // so they do not receive a notification about their own action.
        if (notifyCoordinator && coordinatorUserId.HasValue && coordinatorUserId.Value != Guid.Empty)
        {
            var coordinatorMetadata = JsonSerializer.Serialize(new
            {
                reportId = payment.Id,
                salesRepName = payment.Rep?.FullName,
                customerName = payment.CustomerName,
                amount = payment.Amount,
                oldStatus = oldStatus.ToString(),
                newStatus = newStatus.ToString(),
                reviewer,
                updatedAt = payment.UpdatedAt,
                actionUrl = coordinatorActionUrl,
            });

            try
            {
                await _notifications.SendToUserAsync(new SendNotificationRequest
                {
                    UserId = coordinatorUserId.Value,
                    Title = "Payment Report Status Updated",
                    Message = $"{reviewer} changed {payment.Rep?.FullName ?? "a Sales Rep"}'s payment report for {payment.CustomerName} from {oldStatus} to {newStatus}.",
                    Type = NotificationType.PaymentReportStatusChanged.ToString(),
                    Metadata = coordinatorMetadata,
                }, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Failed to create the status-change notification for Payment Report {ReportId} and Coordinator user {CoordinatorUserId}.",
                    payment.Id,
                    coordinatorUserId.Value);
            }
        }
        else if (notifyCoordinator)
        {
            _logger.LogWarning(
                "No assigned Coordinator user was found while creating the status-change notification for Payment Report {ReportId}.",
                payment.Id);
        }

        // Realtime list/detail refresh for both the Sales Rep and assigned Coordinator.
        var targetUserIds = new HashSet<Guid>
        {
            repUserId,
        };

        if (coordinatorUserId.HasValue && coordinatorUserId.Value != Guid.Empty)
        {
            targetUserIds.Add(coordinatorUserId.Value);
        }

        try
        {
            await _publisher.PublishPaymentReportEventAsync(
                new PaymentReportEventDto
                {
                    EventType = "paymentReportStatusChanged",
                    ReportId = payment.Id,
                    SalesRepUserId = repUserId,
                    CoordinatorUserId = coordinatorUserId,
                    OldStatus = oldStatus.ToString(),
                    NewStatus = newStatus.ToString(),
                    ActionUrl = repActionUrl,
                    OccurredAt = DateTime.UtcNow,
                },
                targetUserIds,
                ct);

            _logger.LogInformation(
                "Published Payment Report status event for report {ReportId} to Sales Rep user {RepUserId} and Coordinator user {CoordinatorUserId}.",
                payment.Id,
                repUserId,
                coordinatorUserId);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to publish the realtime status-change event for Payment Report {ReportId}.",
                payment.Id);
        }
    }

    public async Task AdminSoftDeleteAsync(Guid id, string deletedBy, CancellationToken ct = default)
    {
        var payment = await _uow.Repository<RepPayment>().Query()
            .FirstOrDefaultAsync(p => p.Id == id, ct)
            ?? throw new NotFoundException("Payment report", id);

        payment.IsDeletedByAdmin = true;
        payment.AdminDeletedAt = DateTime.UtcNow;
        payment.UpdatedBy = deletedBy;
        await _uow.SaveChangesAsync(ct);
    }

    public async Task<PagedResult<RepPaymentDto>> AdminGetTrashAsync(
        RepPaymentQueryDto filter,
        CancellationToken ct = default)
    {
        // Preserve the existing 30-day admin-trash retention rule.
        var cutoff = DateTime.UtcNow.AddDays(-30);
        var expired = await _uow.Repository<RepPayment>().Query()
            .Where(p => p.IsDeletedByAdmin
                && p.AdminDeletedAt.HasValue
                && p.AdminDeletedAt.Value < cutoff)
            .ToListAsync(ct);

        foreach (var payment in expired)
        {
            DeleteImageFile(payment.ImageUrl);
            _uow.Repository<RepPayment>().Remove(payment);
        }

        if (expired.Count > 0)
            await _uow.SaveChangesAsync(ct);

        var query = _uow.Repository<RepPayment>().Query()
            .AsNoTracking()
            .Include(p => p.Rep)
            .Where(p => p.IsDeletedByAdmin);

        return await ExecutePagedQueryAsync(query, filter, ct);
    }

    public async Task AdminRestoreAsync(Guid id, CancellationToken ct = default)
    {
        var payment = await _uow.Repository<RepPayment>().Query()
            .FirstOrDefaultAsync(p => p.Id == id, ct)
            ?? throw new NotFoundException("Payment report", id);

        payment.IsDeletedByAdmin = false;
        payment.AdminDeletedAt = null;
        await _uow.SaveChangesAsync(ct);
    }

    // Backward-compatible list overload for existing tests/callers.
    public async Task<List<RepPaymentDto>> AdminGetTrashAsync(
        CancellationToken ct = default)
    {
        var filter = new RepPaymentQueryDto
        {
            Page = 1,
            PageSize = 500,
        };

        var firstPage = await AdminGetTrashAsync(filter, ct);
        var items = firstPage.Items.ToList();

        while (items.Count < firstPage.TotalCount)
        {
            filter.Page++;
            var nextPage = await AdminGetTrashAsync(filter, ct);
            if (!nextPage.Items.Any()) break;
            items.AddRange(nextPage.Items);
        }

        return items;
    }

    public async Task AdminHardDeleteAsync(Guid id, CancellationToken ct = default)
    {
        var payment = await _uow.Repository<RepPayment>().Query()
            .FirstOrDefaultAsync(p => p.Id == id, ct)
            ?? throw new NotFoundException("Payment report", id);

        DeleteImageFile(payment.ImageUrl);
        _uow.Repository<RepPayment>().Remove(payment);
        await _uow.SaveChangesAsync(ct);
    }

    public async Task BulkSoftDeleteAdminAsync(List<Guid> ids, string deletedBy, CancellationToken ct = default)
    {
        var payments = await _uow.Repository<RepPayment>().Query()
            .Where(p => ids.Contains(p.Id) && !p.IsDeletedByAdmin)
            .ToListAsync(ct);

        foreach (var p in payments)
        {
            p.IsDeletedByAdmin = true;
            p.AdminDeletedAt = DateTime.UtcNow;
            p.UpdatedBy = deletedBy;
        }

        await _uow.SaveChangesAsync(ct);
    }

    public async Task BulkUpdateStatusAsync(List<Guid> ids, string status, string updatedBy, CancellationToken ct = default)
    {
        if (!Enum.TryParse<RepPaymentStatus>(status, ignoreCase: true, out var newStatus))
            throw new ArgumentException($"Invalid status '{status}'.");

        var payments = await _uow.Repository<RepPayment>().Query()
            .Include(p => p.Rep)
            .Where(p => ids.Contains(p.Id))
            .ToListAsync(ct);

        var changed = payments.Where(p => p.Status != newStatus).Select(p => (p.Id, OldStatus: p.Status)).ToList();

        foreach (var p in payments)
        {
            p.Status = newStatus;
            p.UpdatedAt = DateTime.UtcNow;
            p.UpdatedBy = updatedBy;
        }

        await _uow.SaveChangesAsync(ct);

        // Notify and publish realtime updates for each report whose status changed.
        // Await the work so scoped services remain valid and failures are logged
        // by NotifyStatusChangeAsync instead of being lost in a fire-and-forget task.
        foreach (var (id, oldStatus) in changed)
        {
            var payment = payments.First(p => p.Id == id);
            var repUserId = payment.Rep?.UserId ?? Guid.Empty;

            if (repUserId == Guid.Empty)
            {
                _logger.LogWarning(
                    "Skipping Payment Report bulk status notification because the Sales Rep UserId was not found. ReportId: {ReportId}",
                    payment.Id);

                continue;
            }

            var coordinatorUserIds = await GetAssignedCoordinatorUserIdsAsync(payment.RepId, ct);
            Guid? coordinatorUserId = coordinatorUserIds.Count > 0 ? coordinatorUserIds[0] : null;

            await NotifyStatusChangeAsync(
                payment,
                repUserId,
                oldStatus,
                newStatus,
                updatedBy,
                coordinatorUserId,
                notifyCoordinator: true,
                ct: ct);
        }
    }

    // ── Coordinator ───────────────────────────────────────────────────────────

    public async Task<PagedResult<RepPaymentDto>> GetForCoordinatorAsync(
        Guid coordinatorUserId,
        RepPaymentQueryDto filter,
        bool trash = false,
        CancellationToken ct = default)
    {
        var repIds = await GetAssignedRepIdsAsync(coordinatorUserId, ct);

        var query = _uow.Repository<RepPayment>().Query()
            .AsNoTracking()
            .Include(p => p.Rep)
            .Where(p => repIds.Contains(p.RepId)
                && p.IsDeletedByCoordinator == trash);

        return await ExecutePagedQueryAsync(query, filter, ct);
    }

    // Backward-compatible list overload for existing tests/callers.
    public async Task<List<RepPaymentDto>> GetForCoordinatorAsync(
        Guid coordinatorUserId,
        string? status = null,
        bool trash = false,
        CancellationToken ct = default)
    {
        var filter = new RepPaymentQueryDto
        {
            Page = 1,
            PageSize = 500,
            Status = status,
        };

        var firstPage = await GetForCoordinatorAsync(
            coordinatorUserId,
            filter,
            trash,
            ct);

        var items = firstPage.Items.ToList();

        while (items.Count < firstPage.TotalCount)
        {
            filter.Page++;
            var nextPage = await GetForCoordinatorAsync(
                coordinatorUserId,
                filter,
                trash,
                ct);

            if (!nextPage.Items.Any()) break;
            items.AddRange(nextPage.Items);
        }

        return items;
    }

    public async Task<List<RepPaymentRepOptionDto>> GetCoordinatorRepOptionsAsync(
        Guid coordinatorUserId,
        CancellationToken ct = default)
    {
        var repIds = await GetAssignedRepIdsAsync(coordinatorUserId, ct);

        return await _uow.Repository<SalesRepProfile>().Query()
            .AsNoTracking()
            .Where(rep => repIds.Contains(rep.Id))
            .Select(rep => new RepPaymentRepOptionDto
            {
                Id = rep.Id,
                Name = rep.FullName,
            })
            .OrderBy(option => option.Name)
            .ToListAsync(ct);
    }

    public async Task<RepPaymentDto> GetForCoordinatorByIdAsync(Guid id, Guid coordinatorUserId, CancellationToken ct = default)
    {
        var repIds = await GetAssignedRepIdsAsync(coordinatorUserId, ct);

        var payment = await _uow.Repository<RepPayment>().Query()
            .Include(p => p.Rep)
            .FirstOrDefaultAsync(p => p.Id == id && repIds.Contains(p.RepId), ct)
            ?? throw new NotFoundException("Payment report", id);

        return MapToDto(payment, payment.Rep?.FullName ?? string.Empty, await GetCoordinatorNameAsync(payment.RepId, ct));
    }

    public async Task<RepPaymentDto> CoordinatorUpdateStatusAsync(Guid id, UpdateRepPaymentStatusDto dto, Guid coordinatorUserId, CancellationToken ct = default)
    {
        var repIds = await GetAssignedRepIdsAsync(coordinatorUserId, ct);

        var payment = await _uow.Repository<RepPayment>().Query()
            .Include(p => p.Rep)
            .FirstOrDefaultAsync(p => p.Id == id && repIds.Contains(p.RepId), ct)
            ?? throw new NotFoundException("Payment report", id);

        if (!Enum.TryParse<RepPaymentStatus>(dto.Status, ignoreCase: true, out var newStatus))
            throw new ArgumentException($"Invalid status '{dto.Status}'.");

        var oldStatus = payment.Status;

        payment.Status = newStatus;
        payment.AdminNotes = dto.AdminNotes;
        payment.UpdatedAt = DateTime.UtcNow;
        payment.UpdatedBy = $"Coordinator:{coordinatorUserId}";

        await _uow.SaveChangesAsync(ct);

        if (oldStatus != newStatus)
        {
            var repUserId = payment.Rep?.UserId ?? Guid.Empty;

            await NotifyStatusChangeAsync(
                payment,
                repUserId,
                oldStatus,
                newStatus,
                "Sales Coordinator",
                coordinatorUserId,
                notifyCoordinator: false,
                ct: ct);
        }

        return MapToDto(payment, payment.Rep?.FullName ?? string.Empty, await GetCoordinatorNameAsync(payment.RepId, ct));
    }

    public async Task CoordinatorTrashAsync(Guid id, Guid coordinatorUserId, CancellationToken ct = default)
    {
        var repIds = await GetAssignedRepIdsAsync(coordinatorUserId, ct);

        var payment = await _uow.Repository<RepPayment>().Query()
            .FirstOrDefaultAsync(p => p.Id == id && repIds.Contains(p.RepId), ct)
            ?? throw new NotFoundException("Payment report", id);

        payment.IsDeletedByCoordinator = true;
        payment.CoordinatorDeletedAt = DateTime.UtcNow;
        await _uow.SaveChangesAsync(ct);
    }

    public async Task CoordinatorRestoreAsync(Guid id, Guid coordinatorUserId, CancellationToken ct = default)
    {
        var repIds = await GetAssignedRepIdsAsync(coordinatorUserId, ct);

        var payment = await _uow.Repository<RepPayment>().Query()
            .FirstOrDefaultAsync(p => p.Id == id && repIds.Contains(p.RepId), ct)
            ?? throw new NotFoundException("Payment report", id);

        payment.IsDeletedByCoordinator = false;
        payment.CoordinatorDeletedAt = null;
        await _uow.SaveChangesAsync(ct);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task<List<Guid>> GetAssignedRepIdsAsync(Guid coordinatorUserId, CancellationToken ct)
    {
        var coord = await _uow.Repository<CoordinatorProfile>().Query()
            .FirstOrDefaultAsync(c => c.UserId == coordinatorUserId, ct)
            ?? throw new NotFoundException("Coordinator profile", coordinatorUserId);

        return await _uow.Repository<RepCoordinator>().Query()
            .Where(rc => rc.CoordinatorId == coord.Id)
            .Select(rc => rc.RepId)
            .ToListAsync(ct);
    }

    private async Task<List<Guid>> GetAssignedCoordinatorUserIdsAsync(Guid repId, CancellationToken ct)
    {
        var coordIds = await _uow.Repository<RepCoordinator>().Query()
            .Where(rc => rc.RepId == repId)
            .Select(rc => rc.CoordinatorId)
            .ToListAsync(ct);

        return await _uow.Repository<CoordinatorProfile>().Query()
            .Where(c => coordIds.Contains(c.Id))
            .Select(c => c.UserId)
            .ToListAsync(ct);
    }

    private async Task<string?> GetCoordinatorNameAsync(Guid repId, CancellationToken ct)
    {
        var coordId = await _uow.Repository<RepCoordinator>().Query()
            .Where(rc => rc.RepId == repId)
            .Select(rc => rc.CoordinatorId)
            .FirstOrDefaultAsync(ct);

        if (coordId == Guid.Empty) return null;

        return await _uow.Repository<CoordinatorProfile>().Query()
            .Where(c => c.Id == coordId)
            .Select(c => c.FullName)
            .FirstOrDefaultAsync(ct);
    }

    private async Task<PagedResult<RepPaymentDto>> ExecutePagedQueryAsync(
        IQueryable<RepPayment> query,
        RepPaymentQueryDto filter,
        CancellationToken ct)
    {
        query = ApplyQueryFilters(query, filter);

        var totalCount = await query.CountAsync(ct);
        var sortedQuery = ApplySorting(query, filter.SortField, filter.SortDir);

        var items = await sortedQuery
            .Skip((filter.Page - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .ToListAsync(ct);

        var mappedItems = items
            .Select(payment => MapToDto(
                payment,
                payment.Rep?.FullName ?? string.Empty,
                null))
            .ToList();

        if (items.Count > 0)
            LogTimestampDiagnostic(items[0], mappedItems[0]);

        return new PagedResult<RepPaymentDto>
        {
            Items = mappedItems,
            TotalCount = totalCount,
            Page = filter.Page,
            PageSize = filter.PageSize,
        };
    }

    private static IQueryable<RepPayment> ApplyQueryFilters(
        IQueryable<RepPayment> query,
        RepPaymentQueryDto filter)
    {
        if (!string.IsNullOrWhiteSpace(filter.Status)
            && Enum.TryParse<RepPaymentStatus>(
                filter.Status,
                ignoreCase: true,
                out var status))
        {
            query = query.Where(payment => payment.Status == status);
        }

        if (filter.RepId.HasValue)
            query = query.Where(payment => payment.RepId == filter.RepId.Value);

        if (filter.FromDate.HasValue)
        {
            var from = filter.FromDate.Value.Date;
            query = query.Where(payment => payment.CreatedAt >= from);
        }

        if (filter.ToDate.HasValue)
        {
            var toExclusive = filter.ToDate.Value.Date.AddDays(1);
            query = query.Where(payment => payment.CreatedAt < toExclusive);
        }

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var search = filter.Search.Trim().ToLower();
            var reportSearch = search.StartsWith("pr-")
                ? search[3..]
                : search;

            query = query.Where(payment =>
                payment.CustomerName.ToLower().Contains(search)
                || payment.Rep.FullName.ToLower().Contains(search)
                || payment.Id.ToString().ToLower().StartsWith(reportSearch));
        }

        return query;
    }

    private static IQueryable<RepPayment> ApplySorting(
        IQueryable<RepPayment> query,
        string? sortField,
        string? sortDir)
    {
        var descending = !string.Equals(
            sortDir,
            "asc",
            StringComparison.OrdinalIgnoreCase);

        var field = sortField?.Trim().ToLowerInvariant();

        return (field, descending) switch
        {
            ("reportnumber", false) => query
                .OrderBy(payment => payment.Id)
                .ThenBy(payment => payment.CreatedAt),
            ("reportnumber", true) => query
                .OrderByDescending(payment => payment.Id)
                .ThenByDescending(payment => payment.CreatedAt),

            ("repname", false) => query
                .OrderBy(payment => payment.Rep.FullName)
                .ThenBy(payment => payment.CreatedAt),
            ("repname", true) => query
                .OrderByDescending(payment => payment.Rep.FullName)
                .ThenByDescending(payment => payment.CreatedAt),

            ("customername", false) => query
                .OrderBy(payment => payment.CustomerName)
                .ThenBy(payment => payment.CreatedAt),
            ("customername", true) => query
                .OrderByDescending(payment => payment.CustomerName)
                .ThenByDescending(payment => payment.CreatedAt),

            ("amount", false) => query
                .OrderBy(payment => payment.Amount)
                .ThenBy(payment => payment.CreatedAt),
            ("amount", true) => query
                .OrderByDescending(payment => payment.Amount)
                .ThenByDescending(payment => payment.CreatedAt),

            ("status", false) => query
                .OrderBy(payment => payment.Status)
                .ThenBy(payment => payment.CreatedAt),
            ("status", true) => query
                .OrderByDescending(payment => payment.Status)
                .ThenByDescending(payment => payment.CreatedAt),

            ("updatedat", false) => query
                .OrderBy(payment => payment.UpdatedAt)
                .ThenBy(payment => payment.CreatedAt),
            ("updatedat", true) => query
                .OrderByDescending(payment => payment.UpdatedAt)
                .ThenByDescending(payment => payment.CreatedAt),

            _ when descending => query
                .OrderByDescending(payment => payment.CreatedAt)
                .ThenByDescending(payment => payment.Id),

            _ => query
                .OrderBy(payment => payment.CreatedAt)
                .ThenBy(payment => payment.Id),
        };
    }

    private void DeleteImageFile(string? imageUrl) => _fileStorage.Delete(imageUrl);

    private void LogTimestampDiagnostic(RepPayment raw, RepPaymentDto mapped)
    {
        _logger.LogInformation(
            "RepPayment raw timestamps ReportId={ReportId} CreatedAt={CreatedAt:o}/{CreatedAtKind} UpdatedAt={UpdatedAt:o}/{UpdatedAtKind}",
            raw.Id,
            raw.CreatedAt,
            raw.CreatedAt.Kind,
            raw.UpdatedAt,
            raw.UpdatedAt?.Kind.ToString() ?? "null");
        _logger.LogInformation(
            "RepPayment mapped timestamps CreatedAt={CreatedAt:o}/{CreatedAtKind} UpdatedAt={UpdatedAt:o}/{UpdatedAtKind}",
            mapped.CreatedAt,
            mapped.CreatedAt.Kind,
            mapped.UpdatedAt,
            mapped.UpdatedAt?.Kind.ToString() ?? "null");
        _logger.LogInformation(
            "RepPayment timestamp runtime Environment={Environment} LocalTimeZone={LocalTimeZone} UtcNow={UtcNow:o} Now={Now:o}",
            _env.EnvironmentName,
            TimeZoneInfo.Local.Id,
            DateTime.UtcNow,
            DateTime.Now);
    }

    // Timestamp normalization is deliberately scoped to payment reports.
    internal static DateTime AsUtc(DateTime value, TimeZoneInfo? localTimeZone = null) =>
        value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => TimeZoneInfo.ConvertTimeToUtc(
                DateTime.SpecifyKind(value, DateTimeKind.Unspecified),
                localTimeZone ?? TimeZoneInfo.Local),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        };

    internal static DateTime? AsUtc(DateTime? value, TimeZoneInfo? localTimeZone = null) =>
        value.HasValue ? AsUtc(value.Value, localTimeZone) : null;

    internal static RepPaymentDto MapToDto(
        RepPayment p,
        string repName,
        string? coordinatorName,
        TimeZoneInfo? localTimeZone = null) => new()
    {
        Id = p.Id,
        ReportNumber = $"PR-{p.Id.ToString()[..8].ToUpperInvariant()}",
        RepId = p.RepId,
        RepName = repName,
        CoordinatorName = coordinatorName,
        CustomerName = p.CustomerName,
        ReferenceNumber = p.ReferenceNumber,
        Amount = p.Amount,
        // Exposes the authenticated download endpoint (never a direct file URL) — payment evidence is private.
        ImageUrl = string.IsNullOrEmpty(p.ImageUrl) ? null : $"/api/rep-payments/{p.Id}/evidence",
        HasEvidence = !string.IsNullOrEmpty(p.ImageUrl),
        Status = p.Status.ToString(),
        AdminNotes = p.AdminNotes,
        CreatedAt = AsUtc(p.CreatedAt, localTimeZone),
        UpdatedAt = AsUtc(p.UpdatedAt, localTimeZone),
        UpdatedBy = p.UpdatedBy,
        DeletedAt = AsUtc(p.AdminDeletedAt, localTimeZone),
        IsDeletedByAdmin = p.IsDeletedByAdmin,
        AdminDeletedAt = AsUtc(p.AdminDeletedAt, localTimeZone),
        IsDeletedByCoordinator = p.IsDeletedByCoordinator,
        CoordinatorDeletedAt = AsUtc(p.CoordinatorDeletedAt, localTimeZone),
        IsDeletedBySalesRep = p.IsDeletedBySalesRep,
        SalesRepDeletedAt = AsUtc(p.SalesRepDeletedAt, localTimeZone),
    };
}
