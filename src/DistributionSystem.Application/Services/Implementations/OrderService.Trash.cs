using DistributionSystem.Application.DTOs.Common;
using DistributionSystem.Application.DTOs.Order;
using DistributionSystem.Application.DTOs.QuickRequests;
using DistributionSystem.Application.Exceptions;
using DistributionSystem.Domain.Entities;
using DistributionSystem.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace DistributionSystem.Application.Services.Implementations;

public partial class OrderService
{
    private const string AdminTrashRole = "admin";
    private const string RepTrashRole = "rep";
    private const string CoordinatorTrashRole = "coordinator";
    private const string OrderKind = "Order";
    private const string QuickOrderKind = "QuickOrder";

    public Task<PagedResult<UnifiedOrderDto>> GetUnifiedAdminAsync(
        UnifiedOrderFilterRequest filter, bool trash, CancellationToken ct = default) =>
        GetUnifiedAsync(AdminTrashRole, null, null, filter, trash, ct);

    public async Task<PagedResult<UnifiedOrderDto>> GetUnifiedRepAsync(
        Guid userId, UnifiedOrderFilterRequest filter, bool trash, CancellationToken ct = default)
    {
        var repId = await ResolveRepIdAsync(userId, ct);
        return await GetUnifiedAsync(RepTrashRole, repId, null, filter, trash, ct);
    }

    public async Task<PagedResult<UnifiedOrderDto>> GetUnifiedCoordinatorAsync(
        Guid userId, UnifiedOrderFilterRequest filter, bool trash, CancellationToken ct = default)
    {
        var coordinatorId = await ResolveCoordinatorIdAsync(userId, ct);
        return await GetUnifiedAsync(CoordinatorTrashRole, null, coordinatorId, filter, trash, ct);
    }

    private async Task<PagedResult<UnifiedOrderDto>> GetUnifiedAsync(
        string role, Guid? repId, Guid? coordinatorId,
        UnifiedOrderFilterRequest filter, bool trash, CancellationToken ct)
    {
        var orders = _unitOfWork.Repository<Order>().Query().AsNoTracking();
        var quickOrders = _unitOfWork.Repository<QuickRequest>().Query().AsNoTracking()
            .Where(r => r.Type == QuickRequestType.Order);

        if (role == AdminTrashRole)
        {
            orders = orders.Where(o => trash ? o.IsDeleted && !o.IsPurgedByAdmin : !o.IsDeleted && !o.IsPurgedByAdmin);
            quickOrders = quickOrders.Where(r => trash ? r.IsDeletedByAdmin && !r.IsPurgedByAdmin : !r.IsDeletedByAdmin && !r.IsPurgedByAdmin);
        }
        else if (role == RepTrashRole)
        {
            orders = orders.Where(o => o.RepId == repId && (trash ? o.IsDeletedByRep && !o.IsPurgedByRep : !o.IsDeletedByRep && !o.IsPurgedByRep));
            quickOrders = quickOrders.Where(r => r.RepId == repId && (trash ? r.IsDeletedByRep && !r.IsPurgedByRep : !r.IsDeletedByRep && !r.IsPurgedByRep));
        }
        else
        {
            orders = orders.Where(o =>
                (o.Customer.AssignedCoordinatorId == coordinatorId ||
                 (o.RepId.HasValue && o.Rep != null && o.Rep.Coordinators.Any(rc => rc.CoordinatorId == coordinatorId))) &&
                (trash ? o.IsDeletedByCoordinator && !o.IsPurgedByCoordinator : !o.IsDeletedByCoordinator && !o.IsPurgedByCoordinator));
            quickOrders = quickOrders.Where(r => r.Rep.Coordinators.Any(rc => rc.CoordinatorId == coordinatorId) &&
                (trash ? r.IsDeletedByCoordinator && !r.IsPurgedByCoordinator : !r.IsDeletedByCoordinator && !r.IsPurgedByCoordinator));
        }

        return await FilterAndPageUnifiedAsync(role, orders, quickOrders, filter, trash, ct);
    }

    private async Task<PagedResult<UnifiedOrderDto>> FilterAndPageUnifiedAsync(
        string role, IQueryable<Order> orders, IQueryable<QuickRequest> quickOrders,
        UnifiedOrderFilterRequest filter, bool trash, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var search = filter.Search.Trim().ToLower();
            orders = orders.Where(o => o.OrderNumber.ToLower().Contains(search) || o.Customer.ShopName.ToLower().Contains(search) || o.Customer.User.Username.ToLower().Contains(search));
            quickOrders = quickOrders.Where(r => r.RequestNumber.ToLower().Contains(search) || r.CustomerName.ToLower().Contains(search));
        }

        if (!string.IsNullOrWhiteSpace(filter.Status))
            ApplyStatusFilter(filter.Status, ref orders, ref quickOrders);

        if (filter.CustomerId.HasValue)
        {
            var customer = await _unitOfWork.Repository<CustomerProfile>().Query().AsNoTracking()
                .Where(c => c.Id == filter.CustomerId.Value)
                .Select(c => new { c.ShopName, Username = c.User.Username }).FirstOrDefaultAsync(ct);
            orders = orders.Where(o => o.CustomerId == filter.CustomerId.Value);
            if (customer == null) quickOrders = quickOrders.Where(_ => false);
            else quickOrders = quickOrders.Where(r => r.CustomerName == customer.ShopName || r.CustomerName == customer.Username);
        }

        if (filter.FromDate.HasValue)
        {
            orders = orders.Where(o => o.OrderDate >= filter.FromDate.Value);
            quickOrders = quickOrders.Where(r => r.CreatedAt >= filter.FromDate.Value);
        }
        if (filter.ToDate.HasValue)
        {
            var end = filter.ToDate.Value.Date.AddDays(1);
            orders = orders.Where(o => o.OrderDate < end);
            quickOrders = quickOrders.Where(r => r.CreatedAt < end);
        }

        return await PageUnifiedAsync(role, orders, quickOrders, filter, trash, ct);
    }

    private static void ApplyStatusFilter(string status, ref IQueryable<Order> orders, ref IQueryable<QuickRequest> quickOrders)
    {
        var hasOrder = Enum.TryParse<OrderStatus>(status, true, out var orderStatus);
        var hasQuick = Enum.TryParse<QuickRequestStatus>(status, true, out var quickStatus);
        if (!hasOrder) { if (!hasQuick) throw new ArgumentException(status); }
        orders = hasOrder ? orders.Where(o => o.Status == orderStatus) : orders.Where(_ => false);
        quickOrders = hasQuick ? quickOrders.Where(r => r.Status == quickStatus) : quickOrders.Where(_ => false);
    }

    private async Task<PagedResult<UnifiedOrderDto>> PageUnifiedAsync(
        string role, IQueryable<Order> orders, IQueryable<QuickRequest> quickOrders,
        UnifiedOrderFilterRequest filter, bool trash, CancellationToken ct)
    {
        var orderRows = orders.Select(o => new UnifiedOrderIndexRow
        {
            Id = o.Id, Kind = OrderKind, Number = o.OrderNumber,
            CustomerName = o.Customer.User.Username, ShopName = o.Customer.ShopName,
            CustomerId = o.CustomerId, RepId = o.RepId,
            RepName = o.Rep != null ? o.Rep.FullName : null,
            StatusValue = (int)o.Status, Date = o.OrderDate, CreatedAt = o.CreatedAt,
            TotalAmount = o.TotalAmount,
            DeletedAt = role == AdminTrashRole ? o.DeletedAt : role == RepTrashRole ? o.RepDeletedAt : o.CoordinatorDeletedAt
        });
        var quickRows = quickOrders.Select(r => new UnifiedOrderIndexRow
        {
            Id = r.Id, Kind = QuickOrderKind, Number = r.RequestNumber,
            CustomerName = r.CustomerName, ShopName = r.CustomerName,
            CustomerId = null, RepId = r.RepId, RepName = r.Rep != null ? r.Rep.FullName : null,
            StatusValue = (int)r.Status, Date = r.CreatedAt, CreatedAt = r.CreatedAt,
            TotalAmount = null,
            DeletedAt = role == AdminTrashRole ? r.AdminDeletedAt : role == RepTrashRole ? r.RepDeletedAt : r.CoordinatorDeletedAt
        });
        var combined = orderRows.Concat(quickRows);
        var totalCount = await combined.CountAsync(ct);
        combined = SortUnified(combined, filter, trash);
        var pageRows = await combined.Skip((filter.Page - 1) * filter.PageSize)
            .Take(filter.PageSize).ToListAsync(ct);
        return await MaterializeUnifiedPageAsync(pageRows, totalCount, filter, ct);
    }

    private static IQueryable<UnifiedOrderIndexRow> SortUnified(
        IQueryable<UnifiedOrderIndexRow> query, UnifiedOrderFilterRequest filter, bool trash)
    {
        var desc = !string.Equals(filter.SortDirection, "asc", StringComparison.OrdinalIgnoreCase);
        var field = filter.SortField.ToLowerInvariant();
        if (field == "number" || field == "ordernumber")
            return desc ? query.OrderByDescending(x => x.Number) : query.OrderBy(x => x.Number);
        if (field == "customer" || field == "customername")
            return desc ? query.OrderByDescending(x => x.CustomerName) : query.OrderBy(x => x.CustomerName);
        if (field == "status")
            return desc ? query.OrderByDescending(x => x.StatusValue) : query.OrderBy(x => x.StatusValue);
        if (field == "total" || field == "totalamount")
            return desc ? query.OrderByDescending(x => x.TotalAmount) : query.OrderBy(x => x.TotalAmount);
        if (trash)
            return desc ? query.OrderByDescending(x => x.DeletedAt) : query.OrderBy(x => x.DeletedAt);
        return desc ? query.OrderByDescending(x => x.Date) : query.OrderBy(x => x.Date);
    }

    private async Task<PagedResult<UnifiedOrderDto>> MaterializeUnifiedPageAsync(
        List<UnifiedOrderIndexRow> rows, int totalCount, UnifiedOrderFilterRequest filter, CancellationToken ct)
    {
        var orderIds = rows.Where(x => x.Kind == OrderKind).Select(x => x.Id).ToList();
        var quickIds = rows.Where(x => x.Kind == QuickOrderKind).Select(x => x.Id).ToList();
        var orderEntities = await _unitOfWork.Repository<Order>().Query().AsNoTracking()
            .Include(o => o.Customer).ThenInclude(c => c.User)
            .Include(o => o.Rep).ThenInclude(r => r!.User)
            .Include(o => o.OrderItems).Where(o => orderIds.Contains(o.Id))
            .ToDictionaryAsync(o => o.Id, ct);

        // IMPORTANT: unified Orders must load the new generic attachment collection too.
        var quickEntities = await _unitOfWork.Repository<QuickRequest>().Query().AsNoTracking()
            .Include(r => r.Rep)
            .Include(r => r.Images)
            .Include(r => r.Attachments)
            .Where(r => quickIds.Contains(r.Id))
            .ToDictionaryAsync(r => r.Id, ct);

        var items = rows.Select(row => MapUnified(row, orderEntities, quickEntities)).ToList();
        return new PagedResult<UnifiedOrderDto>
        {
            Items = items, TotalCount = totalCount,
            Page = filter.Page, PageSize = filter.PageSize
        };
    }

    private static UnifiedOrderDto MapUnified(UnifiedOrderIndexRow row,
        IReadOnlyDictionary<Guid, Order> orders, IReadOnlyDictionary<Guid, QuickRequest> quickOrders)
    {
        if (row.Kind == OrderKind)
        {
            var order = MapToDto(orders[row.Id]);
            return new UnifiedOrderDto
            {
                Id = row.Id, Kind = row.Kind, Number = row.Number,
                CustomerName = row.CustomerName, ShopName = row.ShopName,
                CustomerId = row.CustomerId, RepId = row.RepId, RepName = row.RepName,
                Status = order.Status, Date = AsUtc(row.Date), CreatedAt = AsUtc(row.CreatedAt),
                TotalAmount = row.TotalAmount, DeletedAt = AsUtc(row.DeletedAt), Order = order
            };
        }

        var quick = MapQuickOrderToDto(quickOrders[row.Id]);
        return new UnifiedOrderDto
        {
            Id = row.Id, Kind = row.Kind, Number = row.Number,
            CustomerName = row.CustomerName, ShopName = row.ShopName,
            RepId = row.RepId, RepName = row.RepName,
            Status = quick.Status, Date = AsUtc(row.Date), CreatedAt = AsUtc(row.CreatedAt),
            DeletedAt = AsUtc(row.DeletedAt), QuickOrder = quick
        };
    }

    private static QuickRequestDto MapQuickOrderToDto(QuickRequest request)
    {
        var legacyImages = request.Images?
            .Select(i => new QuickRequestAttachmentDto
            {
                Id = i.Id,
                Url = $"/api/quick-requests/{request.Id}/images/{i.Id}",
                OriginalFileName = $"Photo {i.Id:N}.jpg",
                ContentType = "image/*",
                SizeBytes = 0,
                UploadedAt = request.CreatedAt
            })
            .ToList() ?? [];

        var newAttachments = request.Attachments?
            .Select(a => new QuickRequestAttachmentDto
            {
                Id = a.Id,
                Url = $"/api/quick-requests/{request.Id}/images/{a.Id}",
                OriginalFileName = a.OriginalFileName,
                ContentType = a.ContentType,
                SizeBytes = a.SizeBytes,
                UploadedAt = AsUtc(a.UploadedAt)
            })
            .ToList() ?? [];

        var allAttachments = legacyImages.Concat(newAttachments).ToList();

        return new QuickRequestDto
        {
            Id = request.Id,
            RequestNumber = request.RequestNumber,
            Type = request.Type.ToString(),
            CustomerName = request.CustomerName,
            Details = request.Details,
            Status = request.Status.ToString(),
            AdminNotes = request.AdminNotes,
            RepId = request.RepId,
            RepName = request.Rep?.FullName ?? string.Empty,
            CreatedBy = request.CreatedBy,
            ImageUrls = allAttachments
                .Where(a => !string.Equals(a.ContentType, "application/pdf", StringComparison.OrdinalIgnoreCase))
                .Select(a => a.Url)
                .ToList(),
            Attachments = allAttachments,
            CreatedAt = AsUtc(request.CreatedAt),
            UpdatedAt = AsUtc(request.UpdatedAt),
            DeletedAt = AsUtc(
                request.AdminDeletedAt ??
                request.CoordinatorDeletedAt ??
                request.RepDeletedAt)
        };
    }

    private sealed class UnifiedOrderIndexRow
    {
        public Guid Id { get; set; }
        public string Kind { get; set; } = string.Empty;
        public string Number { get; set; } = string.Empty;
        public string CustomerName { get; set; } = string.Empty;
        public string? ShopName { get; set; }
        public Guid? CustomerId { get; set; }
        public Guid? RepId { get; set; }
        public string? RepName { get; set; }
        public int StatusValue { get; set; }
        public DateTime Date { get; set; }
        public DateTime CreatedAt { get; set; }
        public decimal? TotalAmount { get; set; }
        public DateTime? DeletedAt { get; set; }
    }
}
