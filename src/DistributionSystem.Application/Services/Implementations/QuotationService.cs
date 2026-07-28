using DistributionSystem.Application.DTOs.Common;
using DistributionSystem.Application.DTOs.Quotation;
using DistributionSystem.Application.DTOs.Order;
using DistributionSystem.Application.Exceptions;
using DistributionSystem.Application.Services.Interfaces;
using DistributionSystem.Domain.Entities;
using DistributionSystem.Domain.Enums;
using DistributionSystem.Infrastructure.Data.Repositories.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace DistributionSystem.Application.Services.Implementations;

public class QuotationService : IQuotationService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly INotificationService _notificationService;
    private readonly IEmailService _emailService;

    public QuotationService(IUnitOfWork unitOfWork, INotificationService notificationService, IEmailService emailService)
    {
        _unitOfWork = unitOfWork;
        _notificationService = notificationService;
        _emailService = emailService;
    }

    // ===== Customer endpoints =====

    public async Task<QuotationDto> CustomerCreateQuotationAsync(Guid userId, CreateQuotationRequest request, CancellationToken ct)
    {
        var customer = await _unitOfWork.Repository<CustomerProfile>().Query()
            .Include(c => c.User)
            .Include(c => c.AssignedRep)
            .FirstOrDefaultAsync(c => c.UserId == userId, ct)
            ?? throw new NotFoundException("Customer profile", userId);

        var quotation = await CreateQuotationCore(customer.Id, null, customer.AssignedCoordinatorId, request, ct);

        // Notify coordinator
        if (quotation.CoordinatorId.HasValue)
        {
            var coord = await _unitOfWork.Repository<CoordinatorProfile>().GetByIdAsync(quotation.CoordinatorId.Value, ct);
            if (coord != null)
            {
                await _notificationService.SendNotificationAsync(
                    coord.UserId, NotificationType.QuotationSubmitted,
                    "New Quotation Request",
                    $"Customer '{customer.ShopName}' has requested a quotation ({quotation.QuotationNumber})",
                    ct);
            }
        }

        await NotifyAdminsAboutSubmittedQuotationAsync(
            quotation,
            $"Customer '{customer.ShopName}' submitted quotation {quotation.QuotationNumber}",
            ct);

        return MapToDto(quotation);
    }

    public async Task<PagedResult<QuotationDto>> CustomerGetQuotationsAsync(Guid userId, int page, int pageSize, string? status, CancellationToken ct)
    {
        var customer = await _unitOfWork.Repository<CustomerProfile>().Query()
            .FirstOrDefaultAsync(c => c.UserId == userId, ct)
            ?? throw new NotFoundException("Customer profile", userId);

        return await GetQuotationsPagedAsync(q => q.CustomerId == customer.Id && !q.IsDeletedByCustomer && !q.IsDeleted, page, pageSize, status, ct);
    }

    public async Task<QuotationDto> CustomerGetQuotationByIdAsync(Guid userId, Guid quotationId, CancellationToken ct)
    {
        var customer = await _unitOfWork.Repository<CustomerProfile>().Query()
            .FirstOrDefaultAsync(c => c.UserId == userId, ct)
            ?? throw new NotFoundException("Customer profile", userId);

        var quotation = await GetQuotationWithIncludes()
            .FirstOrDefaultAsync(q => q.Id == quotationId && q.CustomerId == customer.Id, ct)
            ?? throw new NotFoundException("Quotation", quotationId);

        return MapToDto(quotation);
    }

    public async Task<Guid> ConvertQuotationToOrderAsync(Guid userId, Guid quotationId, ConvertQuotationToOrderRequest request, CancellationToken ct)
    {
        var customer = await _unitOfWork.Repository<CustomerProfile>().Query()
            .FirstOrDefaultAsync(c => c.UserId == userId, ct)
            ?? throw new NotFoundException("Customer profile", userId);

        var quotation = await GetQuotationWithIncludes()
            .FirstOrDefaultAsync(q => q.Id == quotationId && q.CustomerId == customer.Id, ct)
            ?? throw new NotFoundException("Quotation", quotationId);

        if (quotation.Status == QuotationStatus.ConvertedToOrder && quotation.ConvertedOrderId.HasValue)
            return quotation.ConvertedOrderId.Value;

        if (quotation.Status != QuotationStatus.Approved)
            throw new BusinessException("Only approved quotations can be converted to orders");

        return await ConvertApprovedQuotationToOrderAsync(quotation, userId, ct);
    }

    public async Task<QuotationDto> CustomerCancelQuotationAsync(Guid userId, Guid quotationId, CancelQuotationRequest request, CancellationToken ct)
    {
        var customer = await _unitOfWork.Repository<CustomerProfile>().Query()
            .FirstOrDefaultAsync(c => c.UserId == userId, ct)
            ?? throw new NotFoundException("Customer profile", userId);

        var quotation = await GetQuotationWithIncludes()
            .FirstOrDefaultAsync(q => q.Id == quotationId && q.CustomerId == customer.Id, ct)
            ?? throw new NotFoundException("Quotation", quotationId);

        if (quotation.Status != QuotationStatus.Submitted && quotation.Status != QuotationStatus.UnderReview)
            throw new BusinessException("Only pending quotations can be cancelled");

        quotation.Status = QuotationStatus.Rejected;
        quotation.RejectionReason = string.IsNullOrWhiteSpace(request.Reason)
            ? "Cancelled by customer"
            : request.Reason;

        await _unitOfWork.SaveChangesAsync(ct);

        if (quotation.Rep != null)
        {
            await _notificationService.SendNotificationAsync(
                quotation.Rep.UserId,
                NotificationType.General,
                "Quotation Cancelled",
                $"Customer '{quotation.Customer.ShopName}' cancelled quotation ({quotation.QuotationNumber}).",
                ct);
        }

        if (quotation.Coordinator != null)
        {
            await _notificationService.SendNotificationAsync(
                quotation.Coordinator.UserId,
                NotificationType.General,
                "Quotation Cancelled",
                $"Customer '{quotation.Customer.ShopName}' cancelled quotation ({quotation.QuotationNumber}).",
                ct);
        }

        return MapToDto(quotation);
    }

    // ===== Rep endpoints =====

    public async Task<QuotationDto> RepCreateQuotationAsync(Guid userId, CreateQuotationRequest request, CancellationToken ct)
    {
        var rep = await _unitOfWork.Repository<SalesRepProfile>().Query()
            .Include(r => r.User)
            .Include(r => r.Coordinators)
            .FirstOrDefaultAsync(r => r.UserId == userId, ct)
            ?? throw new NotFoundException("Sales rep profile", userId);

        var customer = await _unitOfWork.Repository<CustomerProfile>().GetByIdAsync(request.CustomerId, ct)
            ?? throw new NotFoundException("Customer", request.CustomerId);

        var quotation = await CreateQuotationCore(request.CustomerId, rep.Id, customer.AssignedCoordinatorId ?? rep.Coordinators.FirstOrDefault()?.CoordinatorId, request, ct);

        // Notify coordinator
        if (quotation.CoordinatorId.HasValue)
        {
            var coord = await _unitOfWork.Repository<CoordinatorProfile>().GetByIdAsync(quotation.CoordinatorId.Value, ct);
            if (coord != null)
            {
                await _notificationService.SendNotificationAsync(
                    coord.UserId, NotificationType.QuotationSubmitted,
                    "New Quotation Submitted",
                    $"Rep '{rep.FullName}' submitted a quotation ({quotation.QuotationNumber}) for customer '{customer.ShopName}'",
                    ct);
            }
        }

        await NotifyAdminsAboutSubmittedQuotationAsync(
            quotation,
            $"Rep '{rep.FullName}' submitted quotation {quotation.QuotationNumber} for customer '{customer.ShopName}'",
            ct);

        return MapToDto(quotation);
    }

    public async Task<PagedResult<QuotationDto>> RepGetQuotationsAsync(Guid userId, int page, int pageSize, string? status, CancellationToken ct)
    {
        var rep = await _unitOfWork.Repository<SalesRepProfile>().Query()
            .FirstOrDefaultAsync(r => r.UserId == userId, ct)
            ?? throw new NotFoundException("Sales rep profile", userId);

        return await GetQuotationsPagedAsync(q => q.RepId == rep.Id && !q.IsDeletedByRep && !q.IsDeleted, page, pageSize, status, ct);
    }

    public async Task<QuotationDto> RepGetQuotationByIdAsync(Guid userId, Guid quotationId, CancellationToken ct)
    {
        var rep = await _unitOfWork.Repository<SalesRepProfile>().Query()
            .FirstOrDefaultAsync(r => r.UserId == userId, ct)
            ?? throw new NotFoundException("Sales rep profile", userId);

        var quotation = await GetQuotationWithIncludes()
            .FirstOrDefaultAsync(q => q.Id == quotationId && q.RepId == rep.Id, ct)
            ?? throw new NotFoundException("Quotation", quotationId);

        return MapToDto(quotation);
    }

    // ===== Coordinator endpoints =====

    public async Task<PagedResult<QuotationDto>> CoordinatorGetQuotationsAsync(Guid userId, int page, int pageSize, string? status, CancellationToken ct)
    {
        var coordinator = await _unitOfWork.Repository<CoordinatorProfile>().Query()
            .FirstOrDefaultAsync(c => c.UserId == userId, ct)
            ?? throw new NotFoundException("Coordinator profile", userId);

        // Include quotations explicitly assigned to this coordinator OR from reps linked to this coordinator
        var repIds = await _unitOfWork.Repository<RepCoordinator>().Query()
            .Where(rc => rc.CoordinatorId == coordinator.Id)
            .Select(rc => rc.RepId)
            .ToListAsync(ct);

        return await GetQuotationsPagedAsync(
            q => (q.CoordinatorId == coordinator.Id || (q.RepId.HasValue && repIds.Contains(q.RepId.Value)))
                 && !q.IsDeletedByCoordinator && !q.IsDeleted,
            page, pageSize, status, ct);
    }

    public async Task<QuotationDto> CoordinatorGetQuotationByIdAsync(Guid userId, Guid quotationId, CancellationToken ct)
    {
        var coordinator = await _unitOfWork.Repository<CoordinatorProfile>().Query()
            .FirstOrDefaultAsync(c => c.UserId == userId, ct)
            ?? throw new NotFoundException("Coordinator profile", userId);

        var repIds = await _unitOfWork.Repository<RepCoordinator>().Query()
            .Where(rc => rc.CoordinatorId == coordinator.Id)
            .Select(rc => rc.RepId).ToListAsync(ct);

        var quotation = await GetQuotationWithIncludes()
            .FirstOrDefaultAsync(q => q.Id == quotationId
                && (q.CoordinatorId == coordinator.Id || (q.RepId.HasValue && repIds.Contains(q.RepId.Value))), ct)
            ?? throw new NotFoundException("Quotation", quotationId);

        return MapToDto(quotation);
    }

    public async Task<QuotationDto> ApproveQuotationAsync(Guid userId, Guid quotationId, ApproveQuotationRequest request, CancellationToken ct)
    {
        var coordinator = await _unitOfWork.Repository<CoordinatorProfile>().Query()
            .FirstOrDefaultAsync(c => c.UserId == userId, ct)
            ?? throw new NotFoundException("Coordinator profile", userId);

        var repIds = await _unitOfWork.Repository<RepCoordinator>().Query()
            .Where(rc => rc.CoordinatorId == coordinator.Id)
            .Select(rc => rc.RepId).ToListAsync(ct);

        var quotation = await GetQuotationWithIncludes()
            .FirstOrDefaultAsync(q => q.Id == quotationId
                && (q.CoordinatorId == coordinator.Id || (q.RepId.HasValue && repIds.Contains(q.RepId.Value))), ct)
            ?? throw new NotFoundException("Quotation", quotationId);

        if (quotation.Status != QuotationStatus.Submitted)
            throw new BusinessException("Quotation is not in submitted status");

        if (request.Notes != null) quotation.Notes = request.Notes;

        quotation.Status = QuotationStatus.Approved;
        await _unitOfWork.SaveChangesAsync(ct);

        // Notify customer
        await _notificationService.SendNotificationAsync(
            quotation.Customer.UserId, NotificationType.QuotationApproved,
            "Quotation Approved",
            $"Your quotation ({quotation.QuotationNumber}) has been approved.",
            ct);

        // Notify rep if exists
        if (quotation.Rep != null)
        {
            await _notificationService.SendNotificationAsync(
                quotation.Rep.UserId, NotificationType.QuotationApproved,
                "Quotation Approved",
                $"Quotation ({quotation.QuotationNumber}) for customer '{quotation.Customer.ShopName}' has been approved and converted to order.",
                ct);
        }

        // Notify coordinator who approved
        await _notificationService.SendNotificationAsync(
            coordinator.UserId, NotificationType.QuotationApproved,
            "Quotation Approved",
            $"Quotation ({quotation.QuotationNumber}) approved.",
            ct);

        return MapToDto(quotation);
    }

    public async Task RejectQuotationAsync(Guid userId, Guid quotationId, RejectQuotationRequest request, CancellationToken ct)
    {
        var coordinator = await _unitOfWork.Repository<CoordinatorProfile>().Query()
            .FirstOrDefaultAsync(c => c.UserId == userId, ct)
            ?? throw new NotFoundException("Coordinator profile", userId);

        var repIds = await _unitOfWork.Repository<RepCoordinator>().Query()
            .Where(rc => rc.CoordinatorId == coordinator.Id)
            .Select(rc => rc.RepId).ToListAsync(ct);

        var quotation = await GetQuotationWithIncludes()
            .FirstOrDefaultAsync(q => q.Id == quotationId
                && (q.CoordinatorId == coordinator.Id || (q.RepId.HasValue && repIds.Contains(q.RepId.Value))), ct)
            ?? throw new NotFoundException("Quotation", quotationId);

        if (quotation.Status != QuotationStatus.Submitted)
            throw new BusinessException("Quotation is not in submitted status");

        quotation.Status = QuotationStatus.Rejected;
        quotation.RejectionReason = request.Reason;
        await _unitOfWork.SaveChangesAsync(ct);

        // Notify customer
        await _notificationService.SendNotificationAsync(
            quotation.Customer.UserId, NotificationType.QuotationRejected,
            "Quotation Rejected",
            $"Your quotation ({quotation.QuotationNumber}) has been rejected. Reason: {request.Reason}",
            ct);

        // Notify rep if exists
        if (quotation.Rep != null)
        {
            await _notificationService.SendNotificationAsync(
                quotation.Rep.UserId, NotificationType.QuotationRejected,
                "Quotation Rejected",
                $"Quotation ({quotation.QuotationNumber}) for customer '{quotation.Customer.ShopName}' was rejected. Reason: {request.Reason}",
                ct);
        }
    }

    // ===== Admin endpoints =====

    public async Task<PagedResult<QuotationDto>> AdminGetAllQuotationsAsync(int page, int pageSize, string? status, string? search, CancellationToken ct)
    {
        var query = GetQuotationWithIncludes()
            .Where(q => !q.IsDeleted);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var searchTerm = $"%{search.Trim()}%";
            query = query.Where(q =>
                EF.Functions.ILike(q.QuotationNumber, searchTerm) ||
                EF.Functions.ILike(q.Customer.ShopName, searchTerm));
        }

        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<QuotationStatus>(status, true, out var statusEnum))
            query = query.Where(q => q.Status == statusEnum);

        var totalCount = await query.CountAsync(ct);
        var items = await query.OrderByDescending(q => q.CreatedAt).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);

        return new PagedResult<QuotationDto>
        {
            Items = items.Select(MapToDto),
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize
        };
    }

    public async Task<QuotationDto> AdminGetQuotationByIdAsync(Guid quotationId, CancellationToken ct)
    {
        var quotation = await GetQuotationWithIncludes()
            .FirstOrDefaultAsync(q => q.Id == quotationId, ct)
            ?? throw new NotFoundException("Quotation", quotationId);

        return MapToDto(quotation);
    }

    public async Task<QuotationDto> AdminApproveQuotationAsync(Guid userId, Guid quotationId, ApproveQuotationRequest request, CancellationToken ct)
    {
        var quotation = await GetQuotationWithIncludes()
            .FirstOrDefaultAsync(q => q.Id == quotationId, ct)
            ?? throw new NotFoundException("Quotation", quotationId);

        // Admin can approve from any status except already-converted
        if (quotation.Status == QuotationStatus.ConvertedToOrder)
            throw new BusinessException("Quotation has already been converted to an order");

        if (request.Notes != null) quotation.Notes = request.Notes;
        quotation.RejectionReason = null; // clear any prior rejection reason

        quotation.Status = QuotationStatus.Approved;
        await _unitOfWork.SaveChangesAsync(ct);

        await _notificationService.SendNotificationAsync(
            quotation.Customer.UserId, NotificationType.QuotationApproved,
            "Quotation Approved",
            $"Your quotation ({quotation.QuotationNumber}) has been approved by admin.",
            ct);

        if (quotation.Rep != null)
        {
            await _notificationService.SendNotificationAsync(
                quotation.Rep.UserId, NotificationType.QuotationApproved,
                "Quotation Approved",
                $"Quotation ({quotation.QuotationNumber}) for customer '{quotation.Customer.ShopName}' has been approved by admin.",
                ct);
        }

        return MapToDto(quotation);
    }

    public async Task AdminRejectQuotationAsync(Guid userId, Guid quotationId, RejectQuotationRequest request, CancellationToken ct)
    {
        var quotation = await GetQuotationWithIncludes()
            .FirstOrDefaultAsync(q => q.Id == quotationId, ct)
            ?? throw new NotFoundException("Quotation", quotationId);

        // Admin can reject from any status except already-rejected or converted
        if (quotation.Status == QuotationStatus.Rejected)
            throw new BusinessException("Quotation is already rejected");

        if (quotation.Status == QuotationStatus.ConvertedToOrder)
            throw new BusinessException("Quotation has already been converted to an order");

        quotation.Status = QuotationStatus.Rejected;
        quotation.RejectionReason = request.Reason;
        await _unitOfWork.SaveChangesAsync(ct);

        await _notificationService.SendNotificationAsync(
            quotation.Customer.UserId, NotificationType.QuotationRejected,
            "Quotation Rejected",
            $"Your quotation ({quotation.QuotationNumber}) has been rejected. Reason: {request.Reason}",
            ct);

        if (quotation.Rep != null)
        {
            await _notificationService.SendNotificationAsync(
                quotation.Rep.UserId, NotificationType.QuotationRejected,
                "Quotation Rejected",
                $"Quotation ({quotation.QuotationNumber}) for customer '{quotation.Customer.ShopName}' was rejected. Reason: {request.Reason}",
                ct);
        }
    }

    public async Task AdminSoftDeleteAsync(Guid quotationId, string deletedBy, CancellationToken ct)
    {
        var quotation = await _unitOfWork.Repository<Quotation>().Query()
            .FirstOrDefaultAsync(q => q.Id == quotationId, ct)
            ?? throw new NotFoundException("Quotation", quotationId);

        quotation.IsDeleted = true;
        quotation.DeletedAt = DateTime.UtcNow;
        quotation.DeletedBy = deletedBy;
        await _unitOfWork.SaveChangesAsync(ct);
    }

    public async Task<PagedResult<QuotationDto>> AdminGetTrashAsync(int page, int pageSize, CancellationToken ct)
    {
        // Auto-purge quotations deleted more than 7 days ago
        var cutoff = DateTime.UtcNow.AddDays(-7);
        var expired = await _unitOfWork.Repository<Quotation>().Query()
            .Where(q => q.IsDeleted && q.DeletedAt.HasValue && q.DeletedAt.Value < cutoff)
            .ToListAsync(ct);
        foreach (var q in expired)
            _unitOfWork.Repository<Quotation>().Remove(q);
        if (expired.Count > 0)
            await _unitOfWork.SaveChangesAsync(ct);

        var query = GetQuotationWithIncludes().Where(q => q.IsDeleted);
        var totalCount = await query.CountAsync(ct);
        var items = await query.OrderByDescending(q => q.DeletedAt).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);

        return new PagedResult<QuotationDto>
        {
            Items = items.Select(MapToDto),
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize
        };
    }

    public async Task AdminRestoreAsync(Guid quotationId, CancellationToken ct)
    {
        var quotation = await _unitOfWork.Repository<Quotation>().Query()
            .FirstOrDefaultAsync(q => q.Id == quotationId, ct)
            ?? throw new NotFoundException("Quotation", quotationId);

        quotation.IsDeleted = false;
        quotation.DeletedAt = null;
        quotation.DeletedBy = null;
        await _unitOfWork.SaveChangesAsync(ct);
    }

    // ── Rep trash ─────────────────────────────────────────────────────────

    public async Task RepSoftDeleteAsync(Guid quotationId, Guid repUserId, CancellationToken ct)
    {
        var rep = await _unitOfWork.Repository<SalesRepProfile>().Query()
            .FirstOrDefaultAsync(r => r.UserId == repUserId, ct)
            ?? throw new NotFoundException("Sales rep profile", repUserId);

        var quotation = await GetQuotationWithIncludes()
            .FirstOrDefaultAsync(q => q.Id == quotationId && q.RepId == rep.Id, ct)
            ?? throw new NotFoundException("Quotation", quotationId);

        quotation.IsDeletedByRep = true;
        quotation.RepDeletedAt = DateTime.UtcNow;
        await _unitOfWork.SaveChangesAsync(ct);
    }

    public async Task<PagedResult<QuotationDto>> RepGetTrashAsync(Guid repUserId, int page, int pageSize, CancellationToken ct)
    {
        var rep = await _unitOfWork.Repository<SalesRepProfile>().Query()
            .FirstOrDefaultAsync(r => r.UserId == repUserId, ct)
            ?? throw new NotFoundException("Sales rep profile", repUserId);

        var cutoff = DateTime.UtcNow.AddDays(-7);
        var expired = await _unitOfWork.Repository<Quotation>().Query()
            .Where(q => q.RepId == rep.Id && q.IsDeletedByRep && q.RepDeletedAt.HasValue && q.RepDeletedAt.Value < cutoff)
            .ToListAsync(ct);
        foreach (var q in expired) { q.IsDeletedByRep = false; q.RepDeletedAt = null; }
        if (expired.Count > 0) await _unitOfWork.SaveChangesAsync(ct);

        var query = GetQuotationWithIncludes().Where(q => q.RepId == rep.Id && q.IsDeletedByRep);
        var totalCount = await query.CountAsync(ct);
        var items = await query.OrderByDescending(q => q.RepDeletedAt).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return new PagedResult<QuotationDto> { Items = items.Select(MapToDto), TotalCount = totalCount, Page = page, PageSize = pageSize };
    }

    public async Task RepRestoreAsync(Guid quotationId, Guid repUserId, CancellationToken ct)
    {
        var rep = await _unitOfWork.Repository<SalesRepProfile>().Query()
            .FirstOrDefaultAsync(r => r.UserId == repUserId, ct)
            ?? throw new NotFoundException("Sales rep profile", repUserId);

        var quotation = await _unitOfWork.Repository<Quotation>().Query()
            .FirstOrDefaultAsync(q => q.Id == quotationId && q.RepId == rep.Id, ct)
            ?? throw new NotFoundException("Quotation", quotationId);

        quotation.IsDeletedByRep = false;
        quotation.RepDeletedAt = null;
        await _unitOfWork.SaveChangesAsync(ct);
    }

    // ── Coordinator trash ─────────────────────────────────────────────────

    public async Task CoordinatorSoftDeleteAsync(Guid quotationId, Guid coordinatorUserId, CancellationToken ct)
    {
        var coord = await _unitOfWork.Repository<CoordinatorProfile>().Query()
            .FirstOrDefaultAsync(c => c.UserId == coordinatorUserId, ct)
            ?? throw new NotFoundException("Coordinator profile", coordinatorUserId);

        var repIds = await _unitOfWork.Repository<RepCoordinator>().Query()
            .Where(rc => rc.CoordinatorId == coord.Id)
            .Select(rc => rc.RepId)
            .ToListAsync(ct);

        var quotation = await GetQuotationWithIncludes()
            .FirstOrDefaultAsync(q => q.Id == quotationId
                && (q.CoordinatorId == coord.Id || (q.RepId.HasValue && repIds.Contains(q.RepId.Value)))
                && !q.IsDeletedByCoordinator && !q.IsDeleted, ct)
            ?? throw new NotFoundException("Quotation", quotationId);

        quotation.IsDeletedByCoordinator = true;
        quotation.CoordinatorDeletedAt = DateTime.UtcNow;
        await _unitOfWork.SaveChangesAsync(ct);
    }

    public async Task<PagedResult<QuotationDto>> CoordinatorGetTrashAsync(Guid coordinatorUserId, int page, int pageSize, CancellationToken ct)
    {
        var coord = await _unitOfWork.Repository<CoordinatorProfile>().Query()
            .FirstOrDefaultAsync(c => c.UserId == coordinatorUserId, ct)
            ?? throw new NotFoundException("Coordinator profile", coordinatorUserId);

        var repIds = await _unitOfWork.Repository<RepCoordinator>().Query()
            .Where(rc => rc.CoordinatorId == coord.Id)
            .Select(rc => rc.RepId)
            .ToListAsync(ct);

        var cutoff = DateTime.UtcNow.AddDays(-7);
        var expired = await _unitOfWork.Repository<Quotation>().Query()
            .Where(q => (q.CoordinatorId == coord.Id || (q.RepId.HasValue && repIds.Contains(q.RepId.Value)))
                        && q.IsDeletedByCoordinator && q.CoordinatorDeletedAt.HasValue && q.CoordinatorDeletedAt.Value < cutoff)
            .ToListAsync(ct);
        foreach (var q in expired) { q.IsDeletedByCoordinator = false; q.CoordinatorDeletedAt = null; }
        if (expired.Count > 0) await _unitOfWork.SaveChangesAsync(ct);

        var query = GetQuotationWithIncludes()
            .Where(q => (q.CoordinatorId == coord.Id || (q.RepId.HasValue && repIds.Contains(q.RepId.Value)))
                        && q.IsDeletedByCoordinator);
        var totalCount = await query.CountAsync(ct);
        var items = await query.OrderByDescending(q => q.CoordinatorDeletedAt).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return new PagedResult<QuotationDto> { Items = items.Select(MapToDto), TotalCount = totalCount, Page = page, PageSize = pageSize };
    }

    public async Task CoordinatorRestoreAsync(Guid quotationId, Guid coordinatorUserId, CancellationToken ct)
    {
        var coord = await _unitOfWork.Repository<CoordinatorProfile>().Query()
            .FirstOrDefaultAsync(c => c.UserId == coordinatorUserId, ct)
            ?? throw new NotFoundException("Coordinator profile", coordinatorUserId);

        var repIds = await _unitOfWork.Repository<RepCoordinator>().Query()
            .Where(rc => rc.CoordinatorId == coord.Id)
            .Select(rc => rc.RepId)
            .ToListAsync(ct);

        var quotation = await _unitOfWork.Repository<Quotation>().Query()
            .FirstOrDefaultAsync(q => q.Id == quotationId
                && (q.CoordinatorId == coord.Id || (q.RepId.HasValue && repIds.Contains(q.RepId.Value)))
                && q.IsDeletedByCoordinator, ct)
            ?? throw new NotFoundException("Quotation", quotationId);

        quotation.IsDeletedByCoordinator = false;
        quotation.CoordinatorDeletedAt = null;
        await _unitOfWork.SaveChangesAsync(ct);
    }

    // ── Customer trash ────────────────────────────────────────────────────

    public async Task CustomerSoftDeleteAsync(Guid quotationId, Guid customerUserId, CancellationToken ct)
    {
        var customer = await _unitOfWork.Repository<CustomerProfile>().Query()
            .FirstOrDefaultAsync(c => c.UserId == customerUserId, ct)
            ?? throw new NotFoundException("Customer profile", customerUserId);

        var quotation = await GetQuotationWithIncludes()
            .FirstOrDefaultAsync(q => q.Id == quotationId && q.CustomerId == customer.Id, ct)
            ?? throw new NotFoundException("Quotation", quotationId);

        quotation.IsDeletedByCustomer = true;
        quotation.CustomerDeletedAt = DateTime.UtcNow;
        await _unitOfWork.SaveChangesAsync(ct);
    }

    public async Task<PagedResult<QuotationDto>> CustomerGetTrashAsync(Guid customerUserId, int page, int pageSize, CancellationToken ct)
    {
        var customer = await _unitOfWork.Repository<CustomerProfile>().Query()
            .FirstOrDefaultAsync(c => c.UserId == customerUserId, ct)
            ?? throw new NotFoundException("Customer profile", customerUserId);

        var cutoff = DateTime.UtcNow.AddDays(-7);
        var expired = await _unitOfWork.Repository<Quotation>().Query()
            .Where(q => q.CustomerId == customer.Id && q.IsDeletedByCustomer && q.CustomerDeletedAt.HasValue && q.CustomerDeletedAt.Value < cutoff)
            .ToListAsync(ct);
        foreach (var q in expired) { q.IsDeletedByCustomer = false; q.CustomerDeletedAt = null; }
        if (expired.Count > 0) await _unitOfWork.SaveChangesAsync(ct);

        var query = GetQuotationWithIncludes().Where(q => q.CustomerId == customer.Id && q.IsDeletedByCustomer);
        var totalCount = await query.CountAsync(ct);
        var items = await query.OrderByDescending(q => q.CustomerDeletedAt).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return new PagedResult<QuotationDto> { Items = items.Select(MapToDto), TotalCount = totalCount, Page = page, PageSize = pageSize };
    }

    public async Task CustomerRestoreAsync(Guid quotationId, Guid customerUserId, CancellationToken ct)
    {
        var customer = await _unitOfWork.Repository<CustomerProfile>().Query()
            .FirstOrDefaultAsync(c => c.UserId == customerUserId, ct)
            ?? throw new NotFoundException("Customer profile", customerUserId);

        var quotation = await _unitOfWork.Repository<Quotation>().Query()
            .FirstOrDefaultAsync(q => q.Id == quotationId && q.CustomerId == customer.Id, ct)
            ?? throw new NotFoundException("Quotation", quotationId);

        quotation.IsDeletedByCustomer = false;
        quotation.CustomerDeletedAt = null;
        await _unitOfWork.SaveChangesAsync(ct);
    }

    private async Task<Quotation> CreateQuotationCore(Guid customerId, Guid? repId, Guid? coordinatorId, CreateQuotationRequest request, CancellationToken ct)
    {
        var quotationNumber = $"QT-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString()[..6].ToUpper()}";

        var quotation = new Quotation
        {
            QuotationNumber = quotationNumber,
            CustomerId = customerId,
            RepId = repId,
            CoordinatorId = coordinatorId,
            Status = QuotationStatus.Submitted,
            Notes = request.Notes,
            ValidUntil = request.ValidUntil ?? DateTime.UtcNow.AddDays(30)
        };

        decimal subTotal = 0, totalTax = 0, totalDiscount = 0;

        foreach (var item in request.Items)
        {
            var product = await _unitOfWork.Repository<Product>().GetByIdAsync(item.ProductId, ct)
                ?? throw new NotFoundException("Product", item.ProductId);

            // Keep quoted rate from product pricing metadata; expected price is stored separately as requested price.
            var isSpecialPrice = product.DiscountPercent == null && (product.DiscountAmount ?? 0m) > 0m;
            var baseRate = product.SellingPrice + (product.DiscountAmount ?? 0m);
            var unitPrice = isSpecialPrice ? product.SellingPrice : baseRate;
            var gross = unitPrice * item.Quantity;
            var discountAmt = gross * (item.DiscountPercent / 100m);
            var taxable = gross - discountAmt;
            var tax = (product.TaxAmount ?? 0m) * item.Quantity;
            var lineTotal = taxable + tax;

            quotation.Items.Add(new QuotationItem
            {
                ProductId = item.ProductId,
                ProductName = product.Name,
                ProductSKU = product.SKU,
                MRP = product.MRP,
                TaxCode = product.TaxCode,
                Quantity = item.Quantity,
                UnitPrice = unitPrice,
                ExpectedPrice = item.ExpectedPrice,
                DiscountPercent = item.DiscountPercent,
                TaxAmount = tax,
                LineTotal = lineTotal
            });

            subTotal += taxable;
            totalTax += tax;
            totalDiscount += discountAmt;
        }

        quotation.SubTotal = subTotal;
        quotation.TaxAmount = totalTax;
        quotation.DiscountAmount = totalDiscount;
        quotation.TotalAmount = subTotal + totalTax;

        await _unitOfWork.Repository<Quotation>().AddAsync(quotation, ct);
        await _unitOfWork.SaveChangesAsync(ct);

        // Reload with includes
        return await GetQuotationWithIncludes().FirstAsync(q => q.Id == quotation.Id, ct);
    }

    private IQueryable<Quotation> GetQuotationWithIncludes()
    {
        return _unitOfWork.Repository<Quotation>().Query()
            .Include(q => q.Customer).ThenInclude(c => c.User)
            .Include(q => q.Rep)
            .Include(q => q.Coordinator)
            .Include(q => q.Items).ThenInclude(i => i.Product);
    }

    private async Task<Guid> ConvertApprovedQuotationToOrderAsync(Quotation quotation, Guid approvedBy, CancellationToken ct)
    {
        var orderNumber = $"ORD-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString()[..8].ToUpper()}";

        var order = new Order
        {
            OrderNumber = orderNumber,
            CustomerId = quotation.CustomerId,
            RepId = quotation.RepId,
            OrderDate = DateTime.SpecifyKind(quotation.CreatedAt, DateTimeKind.Utc),
            Status = OrderStatus.Approved,
            ApprovedBy = approvedBy,
            ApprovedAt = DateTime.UtcNow,
            SubTotal = quotation.SubTotal,
            TaxAmount = quotation.TaxAmount,
            DiscountAmount = quotation.DiscountAmount,
            TotalAmount = quotation.TotalAmount,
            DeliveryNotes = quotation.Notes
        };

        foreach (var item in quotation.Items)
        {
            order.OrderItems.Add(new OrderItem
            {
                OrderId = order.Id,
                ProductId = item.ProductId,
                ProductName = item.ProductName,
                ProductSKU = item.ProductSKU,
                TaxCode = item.TaxCode,
                Quantity = item.Quantity,
                UnitPrice = item.UnitPrice,
                MRP = item.MRP,
                DiscountPercent = item.DiscountPercent,
                TaxAmount = item.TaxAmount,
                LineTotal = item.LineTotal
            });
        }

        await _unitOfWork.Repository<Order>().AddAsync(order, ct);

        quotation.Status = QuotationStatus.ConvertedToOrder;
        quotation.ConvertedOrderId = order.Id;

        await _unitOfWork.SaveChangesAsync(ct);
        return order.Id;
    }

    private static DateTime AsUtc(DateTime value) => value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();

    private static DateTime? AsUtc(DateTime? value) => value.HasValue ? AsUtc(value.Value) : null;

    private async Task<PagedResult<QuotationDto>> GetQuotationsPagedAsync(
        System.Linq.Expressions.Expression<Func<Quotation, bool>> filter,
        int page, int pageSize, string? status, CancellationToken ct)
    {
        var query = GetQuotationWithIncludes().Where(filter);

        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<QuotationStatus>(status, true, out var statusEnum))
            query = query.Where(q => q.Status == statusEnum);

        var totalCount = await query.CountAsync(ct);
        var items = await query.OrderByDescending(q => q.CreatedAt).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);

        return new PagedResult<QuotationDto>
        {
            Items = items.Select(MapToDto),
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize
        };
    }

    private async Task<List<User>> GetActiveAdminsAsync(CancellationToken ct)
    {
        return await _unitOfWork.Repository<User>().Query()
            .Where(u => u.Role == UserRole.Admin && u.IsActive)
            .ToListAsync(ct);
    }

    private async Task NotifyAdminsAboutSubmittedQuotationAsync(Quotation quotation, string description, CancellationToken ct)
    {
        var admins = await GetActiveAdminsAsync(ct);
        if (admins.Count == 0) return;

        var title = "New Quotation Submitted";
        var message = description;
        var emailSubject = $"New Quotation Submitted - {quotation.QuotationNumber}";
        var emailBody =
            $"<p>A new quotation has been submitted.</p>" +
            $"<p><strong>Quotation:</strong> {quotation.QuotationNumber}</p>" +
            $"<p><strong>Customer:</strong> {System.Net.WebUtility.HtmlEncode(quotation.Customer?.ShopName ?? quotation.Customer?.User?.Username ?? "-")}</p>" +
            $"<p><strong>Total:</strong> {quotation.TotalAmount:N2}</p>";

        foreach (var admin in admins)
        {
            await _notificationService.SendNotificationAsync(
                admin.Id,
                NotificationType.QuotationSubmitted,
                title,
                message,
                ct);

            if (!string.IsNullOrWhiteSpace(admin.Email))
            {
                await _emailService.SendEmailAsync(admin.Email, emailSubject, emailBody, ct);
            }
        }
    }

    private static QuotationDto MapToDto(Quotation q) => new()
    {
        Id = q.Id,
        QuotationNumber = q.QuotationNumber,
        CustomerId = q.CustomerId,
        CustomerName = q.Customer?.User?.Username ?? q.Customer?.ShopName ?? "",
        ShopName = q.Customer?.ShopName,
        RepId = q.RepId,
        RepName = q.Rep?.FullName,
        CoordinatorId = q.CoordinatorId,
        CoordinatorName = q.Coordinator?.FullName,
        Status = q.Status.ToString(),
        SubTotal = q.SubTotal,
        TaxAmount = q.TaxAmount,
        DiscountAmount = q.DiscountAmount,
        TotalAmount = q.TotalAmount,
        Notes = q.Notes,
        RejectionReason = q.RejectionReason,
        ValidUntil = AsUtc(q.ValidUntil),
        ConvertedOrderId = q.ConvertedOrderId,
        Items = q.Items.Select(i => new QuotationItemDto
        {
            Id = i.Id,
            ProductId = i.ProductId,
            ProductName = i.ProductName,
            ProductSKU = i.ProductSKU,
            MRP = i.MRP,
            Quantity = i.Quantity,
            UnitPrice = i.UnitPrice,
            ExpectedPrice = i.ExpectedPrice,
            DiscountPercent = i.DiscountPercent,
            TaxCode = i.TaxCode,
            TaxAmount = i.TaxAmount,
            LineTotal = i.LineTotal
        }).ToList(),
        CreatedAt = AsUtc(q.CreatedAt),
        IsDeleted = q.IsDeleted,
        DeletedAt = AsUtc(q.DeletedAt ?? q.RepDeletedAt ?? q.CoordinatorDeletedAt ?? q.CustomerDeletedAt)
    };
}
