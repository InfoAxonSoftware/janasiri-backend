using DistributionSystem.Application.DTOs.Common;
using DistributionSystem.Application.DTOs.Order;
using DistributionSystem.Application.Exceptions;
using DistributionSystem.Application.Services.Interfaces;
using DistributionSystem.Domain.Entities;
using DistributionSystem.Domain.Enums;
using DistributionSystem.Infrastructure.Data.Repositories.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace DistributionSystem.Application.Services.Implementations;

public partial class OrderService : IOrderService
{
    private readonly IUnitOfWork _unitOfWork;

    public OrderService(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<OrderDto> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var order = await _unitOfWork.Repository<Order>().Query()
            .Include(o => o.Customer).ThenInclude(c => c.User)
            .Include(o => o.Rep).ThenInclude(r => r!.User)
            .Include(o => o.OrderItems)
            .FirstOrDefaultAsync(o => o.Id == id && !o.IsPurgedByAdmin, cancellationToken)
            ?? throw new NotFoundException("Order", id);

        var dto = MapToDto(order);
        await AppendQuotationMetadataAsync(new List<OrderDto> { dto }, cancellationToken);
        return dto;
    }

    public async Task<PagedResult<OrderDto>> GetAllAsync(OrderFilterRequest filter, CancellationToken cancellationToken = default)
    {
        var query = _unitOfWork.Repository<Order>().Query()
            .Include(o => o.Customer).ThenInclude(c => c.User)
            .Include(o => o.Rep)
            .Include(o => o.OrderItems)
            .AsQueryable();

        // Apply only the current viewer's visibility state.
        if (filter.RepId.HasValue)
            query = query.Where(o => !o.IsDeletedByRep && !o.IsPurgedByRep);
        else if (filter.CustomerId.HasValue)
            query = query.Where(o => !o.IsDeletedByCustomer);
        else
            query = query.Where(o => !o.IsDeleted && !o.IsPurgedByAdmin);

        if (filter.Status.HasValue)
            query = query.Where(o => o.Status == filter.Status.Value);
        if (filter.CustomerId.HasValue)
            query = query.Where(o => o.CustomerId == filter.CustomerId.Value);
        if (filter.RepId.HasValue)
            query = query.Where(o => o.RepId == filter.RepId.Value);
        if (filter.FromDate.HasValue)
            query = query.Where(o => o.OrderDate >= filter.FromDate.Value);
        if (filter.ToDate.HasValue)
            query = query.Where(o => o.OrderDate <= filter.ToDate.Value);

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query.OrderByDescending(o => o.OrderDate)
            .Skip((filter.Page - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .ToListAsync(cancellationToken);

        var orderDtos = items.Select(MapToDto).ToList();
        await AppendQuotationMetadataAsync(orderDtos, cancellationToken);

        return new PagedResult<OrderDto>
        {
            Items = orderDtos,
            TotalCount = totalCount,
            Page = filter.Page,
            PageSize = filter.PageSize
        };
    }

    public async Task<PagedResult<OrderDto>> GetForCoordinatorAsync(Guid coordinatorUserId, OrderFilterRequest filter, CancellationToken cancellationToken = default)
    {
        var coordinator = await _unitOfWork.Repository<CoordinatorProfile>().Query()
            .FirstOrDefaultAsync(c => c.UserId == coordinatorUserId, cancellationToken)
            ?? throw new NotFoundException("Coordinator profile", coordinatorUserId);

        var query = _unitOfWork.Repository<Order>().Query()
            .Include(o => o.Customer).ThenInclude(c => c.User)
            .Include(o => o.Rep)
            .Include(o => o.OrderItems)
            .Where(o => !o.IsDeletedByCoordinator && !o.IsPurgedByCoordinator &&
                        (o.Customer.AssignedCoordinatorId == coordinator.Id
                        || (o.RepId.HasValue && o.Rep != null && o.Rep.Coordinators.Any(rc => rc.CoordinatorId == coordinator.Id))))
            .AsQueryable();

        if (filter.Status.HasValue)
            query = query.Where(o => o.Status == filter.Status.Value);
        if (filter.CustomerId.HasValue)
            query = query.Where(o => o.CustomerId == filter.CustomerId.Value);
        if (filter.RepId.HasValue)
            query = query.Where(o => o.RepId == filter.RepId.Value);
        if (filter.FromDate.HasValue)
            query = query.Where(o => o.OrderDate >= filter.FromDate.Value);
        if (filter.ToDate.HasValue)
            query = query.Where(o => o.OrderDate <= filter.ToDate.Value);

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query.OrderByDescending(o => o.OrderDate)
            .Skip((filter.Page - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .ToListAsync(cancellationToken);

        var orderDtos = items.Select(MapToDto).ToList();
        await AppendQuotationMetadataAsync(orderDtos, cancellationToken);

        return new PagedResult<OrderDto>
        {
            Items = orderDtos,
            TotalCount = totalCount,
            Page = filter.Page,
            PageSize = filter.PageSize
        };
    }

    public async Task<OrderDto> CreateAsync(CreateOrderRequest request, Guid? repId = null, CancellationToken cancellationToken = default)
    {
        var customer = await _unitOfWork.Repository<CustomerProfile>().Query()
            .Include(c => c.User)
            .FirstOrDefaultAsync(c => c.Id == request.CustomerId, cancellationToken)
            ?? throw new NotFoundException("Customer", request.CustomerId);

        if (!customer.User.IsActive)
            throw new BusinessException("Customer account is inactive", "ORDER_CUSTOMER_INACTIVE");

        // If repId is provided, it's the user ID - we need to get the SalesRepProfile ID
        Guid? salesRepProfileId = null;
        if (repId.HasValue)
        {
            var repProfile = await _unitOfWork.Repository<SalesRepProfile>().Query()
                .FirstOrDefaultAsync(r => r.UserId == repId.Value, cancellationToken);
            if (repProfile == null)
                throw new NotFoundException("Sales rep profile not found for user", repId.Value);
            salesRepProfileId = repProfile.Id;
        }
        // Do not automatically assign the customer's assigned rep for a direct customer order.
        // Direct customer orders should show "Direct" in the UI unless created through a rep flow.

        var orderNumber = $"ORD-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString()[..8].ToUpper()}";

        var order = new Order
        {
            OrderNumber = orderNumber,
            CustomerId = request.CustomerId,
            RepId = salesRepProfileId,
            RequiredDeliveryDate = request.RequiredDeliveryDate,
            DeliveryAddress = request.DeliveryAddress,
            DeliveryNotes = request.DeliveryNotes,
            Status = OrderStatus.Pending
        };

        decimal subTotal = 0;
        decimal taxTotal = 0;

        foreach (var item in request.Items)
        {
            var product = await _unitOfWork.Repository<Product>().Query()
                .FirstOrDefaultAsync(p => p.Id == item.ProductId, cancellationToken)
                ?? throw new NotFoundException("Product", item.ProductId);


                var discountPercent = item.DiscountPercent ?? product.DiscountPercent ?? 0;
            var unitPrice = product.SellingPrice;
            var gross = unitPrice * item.Quantity;
            var discountAmount = gross * (discountPercent / 100);
            var taxableBase = gross - discountAmount;

            // derive tax rate from product metadata; product.TaxAmount is the per-unit
            // tax calculated in the uploaded sheet (already applied to discounted price
            // there), so we can reverse-engineer a percentage rate.
            decimal taxRate = 0m;
            if (product.TaxAmount.HasValue)
            {
                var basePerUnit = unitPrice * (1 - discountPercent / 100);
                if (basePerUnit > 0)
                    taxRate = product.TaxAmount.Value / basePerUnit;
            }
            var lineTax = taxableBase * taxRate;

            var orderItem = new OrderItem
            {
                OrderId = order.Id,
                ProductId = item.ProductId,
                // snapshot current name/sku/taxcode so order history is unaffected by later edits
                ProductName = product.Name,
                ProductSKU = product.SKU,
                TaxCode = product.TaxCode,
                Quantity = item.Quantity,
                UnitPrice = unitPrice,
                MRP = product.MRP,
                DiscountPercent = discountPercent,
                TaxAmount = lineTax,
                LineTotal = taxableBase + lineTax
            };

            order.OrderItems.Add(orderItem);
            subTotal += taxableBase;
            taxTotal += lineTax;
        }

        order.SubTotal = subTotal;
        order.TaxAmount = taxTotal;
        order.TotalAmount = subTotal + taxTotal;

        await _unitOfWork.Repository<Order>().AddAsync(order, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);


        return await GetByIdAsync(order.Id, cancellationToken);
    }

    public async Task<OrderDto> ApproveAsync(Guid id, Guid approvedBy, CancellationToken cancellationToken = default)
    {
        var order = await GetOrderEntity(id, cancellationToken);

        if (order.Status != OrderStatus.Pending)
            throw new BusinessException("Only pending orders can be approved", "ORDER_INVALID_STATUS");

        order.Status = OrderStatus.Approved;
        order.ApprovedBy = approvedBy;
        order.ApprovedAt = DateTime.UtcNow;

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return await GetByIdAsync(id, cancellationToken);
    }

    public async Task<OrderDto> RejectAsync(Guid id, string reason, CancellationToken cancellationToken = default)
    {
        var order = await GetOrderEntity(id, cancellationToken);

        if (order.Status != OrderStatus.Pending)
            throw new BusinessException("Only pending orders can be rejected", "ORDER_INVALID_STATUS");

        order.Status = OrderStatus.Rejected;
        order.RejectionReason = reason;

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return await GetByIdAsync(id, cancellationToken);
    }

    public async Task<OrderDto> UpdateStatusAsync(Guid id, UpdateOrderStatusRequest request, CancellationToken cancellationToken = default)
    {
        var order = await GetOrderEntity(id, cancellationToken);
        order.Status = request.Status;

        if (request.Status == OrderStatus.Delivered)
            order.ActualDeliveryDate = DateTime.UtcNow;

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return await GetByIdAsync(id, cancellationToken);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var order = await GetOrderEntity(id, cancellationToken);
        _unitOfWork.Repository<Order>().Remove(order);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task AdminSoftDeleteAsync(Guid id, string deletedBy, CancellationToken cancellationToken = default)
    {
        var order = await _unitOfWork.Repository<Order>().Query()
            .FirstOrDefaultAsync(o => o.Id == id && !o.IsDeleted && !o.IsPurgedByAdmin, cancellationToken)
            ?? throw new NotFoundException("Order", id);

        order.IsDeleted = true;
        order.DeletedAt = DateTime.UtcNow;
        order.DeletedBy = deletedBy;
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task<PagedResult<OrderDto>> AdminGetTrashAsync(int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var query = _unitOfWork.Repository<Order>().Query()
            .Include(o => o.Customer).ThenInclude(c => c.User)
            .Include(o => o.Rep)
            .Include(o => o.OrderItems)
            .Where(o => o.IsDeleted && !o.IsPurgedByAdmin);

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query.OrderByDescending(o => o.DeletedAt)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);

        return new PagedResult<OrderDto>
        {
            Items = items.Select(MapToDto).ToList(),
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize
        };
    }

    public async Task AdminRestoreAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var order = await _unitOfWork.Repository<Order>().Query()
            .FirstOrDefaultAsync(o => o.Id == id && o.IsDeleted && !o.IsPurgedByAdmin, cancellationToken)
            ?? throw new NotFoundException("Order", id);

        order.IsDeleted = false;
        order.DeletedAt = null;
        order.DeletedBy = null;
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    // ── Rep trash ─────────────────────────────────────────────────────────

    public async Task RepSoftDeleteAsync(Guid id, Guid repUserId, CancellationToken cancellationToken = default)
    {
        var repProfile = await _unitOfWork.Repository<SalesRepProfile>().Query()
            .FirstOrDefaultAsync(r => r.UserId == repUserId, cancellationToken)
            ?? throw new NotFoundException("Sales rep profile", repUserId);

        var order = await _unitOfWork.Repository<Order>().Query()
            .FirstOrDefaultAsync(o => o.Id == id && o.RepId == repProfile.Id && !o.IsPurgedByRep, cancellationToken)
            ?? throw new NotFoundException("Order", id);

        order.IsDeletedByRep = true;
        order.RepDeletedAt = DateTime.UtcNow;
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task<PagedResult<OrderDto>> RepGetTrashAsync(Guid repUserId, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var repProfile = await _unitOfWork.Repository<SalesRepProfile>().Query()
            .FirstOrDefaultAsync(r => r.UserId == repUserId, cancellationToken)
            ?? throw new NotFoundException("Sales rep profile", repUserId);

        var query = _unitOfWork.Repository<Order>().Query()
            .Include(o => o.Customer).ThenInclude(c => c.User)
            .Include(o => o.Rep).Include(o => o.OrderItems)
            .Where(o => o.RepId == repProfile.Id && o.IsDeletedByRep && !o.IsPurgedByRep);

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query.OrderByDescending(o => o.RepDeletedAt)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);

        var dtos = items.Select(MapToDto).ToList();
        return new PagedResult<OrderDto> { Items = dtos, TotalCount = totalCount, Page = page, PageSize = pageSize };
    }

    public async Task RepRestoreAsync(Guid id, Guid repUserId, CancellationToken cancellationToken = default)
    {
        var repProfile = await _unitOfWork.Repository<SalesRepProfile>().Query()
            .FirstOrDefaultAsync(r => r.UserId == repUserId, cancellationToken)
            ?? throw new NotFoundException("Sales rep profile", repUserId);

        var order = await _unitOfWork.Repository<Order>().Query()
            .FirstOrDefaultAsync(o => o.Id == id && o.RepId == repProfile.Id && o.IsDeletedByRep && !o.IsPurgedByRep, cancellationToken)
            ?? throw new NotFoundException("Order", id);

        order.IsDeletedByRep = false;
        order.RepDeletedAt = null;
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    // ── Coordinator trash ─────────────────────────────────────────────────

    public async Task CoordinatorSoftDeleteAsync(Guid id, Guid coordinatorUserId, CancellationToken cancellationToken = default)
    {
        var coord = await _unitOfWork.Repository<CoordinatorProfile>().Query()
            .FirstOrDefaultAsync(c => c.UserId == coordinatorUserId, cancellationToken)
            ?? throw new NotFoundException("Coordinator profile", coordinatorUserId);

        // Coordinator can only delete orders visible to them
        var order = await _unitOfWork.Repository<Order>().Query()
            .Include(o => o.Customer).Include(o => o.Rep).ThenInclude(r => r!.Coordinators)
            .FirstOrDefaultAsync(o => o.Id == id && !o.IsPurgedByCoordinator &&
                (o.Customer.AssignedCoordinatorId == coord.Id ||
                 (o.RepId.HasValue && o.Rep != null && o.Rep.Coordinators.Any(rc => rc.CoordinatorId == coord.Id))),
                cancellationToken)
            ?? throw new NotFoundException("Order", id);

        order.IsDeletedByCoordinator = true;
        order.CoordinatorDeletedAt = DateTime.UtcNow;
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task<PagedResult<OrderDto>> CoordinatorGetTrashAsync(Guid coordinatorUserId, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var coord = await _unitOfWork.Repository<CoordinatorProfile>().Query()
            .FirstOrDefaultAsync(c => c.UserId == coordinatorUserId, cancellationToken)
            ?? throw new NotFoundException("Coordinator profile", coordinatorUserId);

        var query = _unitOfWork.Repository<Order>().Query()
            .Include(o => o.Customer).ThenInclude(c => c.User)
            .Include(o => o.Rep).ThenInclude(r => r!.Coordinators)
            .Include(o => o.OrderItems)
            .Where(o => o.IsDeletedByCoordinator && !o.IsPurgedByCoordinator &&
                (o.Customer.AssignedCoordinatorId == coord.Id ||
                 (o.RepId.HasValue && o.Rep != null && o.Rep.Coordinators.Any(rc => rc.CoordinatorId == coord.Id))));

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query.OrderByDescending(o => o.CoordinatorDeletedAt)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);

        var dtos = items.Select(MapToDto).ToList();
        return new PagedResult<OrderDto> { Items = dtos, TotalCount = totalCount, Page = page, PageSize = pageSize };
    }

    public async Task CoordinatorRestoreAsync(Guid id, Guid coordinatorUserId, CancellationToken cancellationToken = default)
    {
        var coord = await _unitOfWork.Repository<CoordinatorProfile>().Query()
            .FirstOrDefaultAsync(c => c.UserId == coordinatorUserId, cancellationToken)
            ?? throw new NotFoundException("Coordinator profile", coordinatorUserId);

        var order = await _unitOfWork.Repository<Order>().Query()
            .Include(o => o.Customer).Include(o => o.Rep).ThenInclude(r => r!.Coordinators)
            .FirstOrDefaultAsync(o => o.Id == id && o.IsDeletedByCoordinator && !o.IsPurgedByCoordinator &&
                (o.Customer.AssignedCoordinatorId == coord.Id ||
                 (o.RepId.HasValue && o.Rep != null && o.Rep.Coordinators.Any(rc => rc.CoordinatorId == coord.Id))),
                cancellationToken)
            ?? throw new NotFoundException("Order", id);

        order.IsDeletedByCoordinator = false;
        order.CoordinatorDeletedAt = null;
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    // ── Customer trash ────────────────────────────────────────────────────

    public async Task CustomerSoftDeleteAsync(Guid id, Guid customerUserId, CancellationToken cancellationToken = default)
    {
        var customer = await _unitOfWork.Repository<CustomerProfile>().Query()
            .FirstOrDefaultAsync(c => c.UserId == customerUserId, cancellationToken)
            ?? throw new NotFoundException("Customer profile", customerUserId);

        var order = await _unitOfWork.Repository<Order>().Query()
            .FirstOrDefaultAsync(o => o.Id == id && o.CustomerId == customer.Id, cancellationToken)
            ?? throw new NotFoundException("Order", id);

        order.IsDeletedByCustomer = true;
        order.CustomerDeletedAt = DateTime.UtcNow;
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task<PagedResult<OrderDto>> CustomerGetTrashAsync(Guid customerUserId, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var customer = await _unitOfWork.Repository<CustomerProfile>().Query()
            .FirstOrDefaultAsync(c => c.UserId == customerUserId, cancellationToken)
            ?? throw new NotFoundException("Customer profile", customerUserId);

        var query = _unitOfWork.Repository<Order>().Query()
            .Include(o => o.Customer).ThenInclude(c => c.User)
            .Include(o => o.Rep).Include(o => o.OrderItems)
            .Where(o => o.CustomerId == customer.Id && o.IsDeletedByCustomer);

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query.OrderByDescending(o => o.CustomerDeletedAt)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);

        var dtos = items.Select(MapToDto).ToList();
        return new PagedResult<OrderDto> { Items = dtos, TotalCount = totalCount, Page = page, PageSize = pageSize };
    }

    public async Task CustomerRestoreAsync(Guid id, Guid customerUserId, CancellationToken cancellationToken = default)
    {
        var customer = await _unitOfWork.Repository<CustomerProfile>().Query()
            .FirstOrDefaultAsync(c => c.UserId == customerUserId, cancellationToken)
            ?? throw new NotFoundException("Customer profile", customerUserId);

        var order = await _unitOfWork.Repository<Order>().Query()
            .FirstOrDefaultAsync(o => o.Id == id && o.CustomerId == customer.Id, cancellationToken)
            ?? throw new NotFoundException("Order", id);

        order.IsDeletedByCustomer = false;
        order.CustomerDeletedAt = null;
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task<OrderDto> CancelAsync(Guid id, string reason, CancellationToken cancellationToken = default)
    {
        var order = await GetOrderEntity(id, cancellationToken);

        if (order.Status is OrderStatus.Dispatched or OrderStatus.Delivered or OrderStatus.Completed)
            throw new BusinessException("Cannot cancel an order that has been dispatched or delivered", "ORDER_CANNOT_CANCEL");

        // restore customer balance when cancelling approved/processing orders
        if (order.Status == OrderStatus.Approved || order.Status == OrderStatus.Processing)
        {
            // No balance tracking needed
        }

        order.Status = OrderStatus.Cancelled;
        order.CancellationReason = reason;

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return await GetByIdAsync(id, cancellationToken);
    }

    public async Task<OrderDto> ConfirmDeliveryAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var order = await GetOrderEntity(id, cancellationToken);
        order.Status = OrderStatus.Completed;
        order.ActualDeliveryDate ??= DateTime.UtcNow;

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return await GetByIdAsync(id, cancellationToken);
    }

    public async Task<OrderDto> RateOrderAsync(Guid id, RateOrderRequest request, CancellationToken cancellationToken = default)
    {
        var order = await GetOrderEntity(id, cancellationToken);
        order.Rating = request.Rating;
        order.RatingComment = request.Comment;

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return await GetByIdAsync(id, cancellationToken);
    }

    public async Task<PagedResult<OrderDto>> GetCustomerOrdersAsync(Guid customerId, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        return await GetAllAsync(new OrderFilterRequest { CustomerId = customerId, Page = page, PageSize = pageSize }, cancellationToken);
    }

    public async Task<OrderDto> ReorderAsync(Guid orderId, Guid customerId, CancellationToken cancellationToken = default)
    {
        var originalOrder = await _unitOfWork.Repository<Order>().Query()
            .Include(o => o.OrderItems)
            .FirstOrDefaultAsync(o => o.Id == orderId, cancellationToken)
            ?? throw new NotFoundException("Order", orderId);

        var request = new CreateOrderRequest
        {
            CustomerId = customerId,
            DeliveryAddress = originalOrder.DeliveryAddress,
            Items = originalOrder.OrderItems
                .Where(oi => oi.ProductId.HasValue)
                .Select(oi => new CreateOrderItemRequest
                {
                    ProductId = oi.ProductId!.Value,
                    Quantity = oi.Quantity,
                    DiscountPercent = oi.DiscountPercent
                }).ToList()
        };

        return await CreateAsync(request, cancellationToken: cancellationToken);
    }

    private async Task<Order> GetOrderEntity(Guid id, CancellationToken cancellationToken)
    {
        return await _unitOfWork.Repository<Order>().Query()
            .Include(o => o.OrderItems)
            .FirstOrDefaultAsync(o => o.Id == id, cancellationToken)
            ?? throw new NotFoundException("Order", id);
    }

    private async Task AppendQuotationMetadataAsync(List<OrderDto> orders, CancellationToken cancellationToken)
    {
        if (orders.Count == 0)
            return;

        var orderIds = orders.Select(o => o.Id).ToList();
        var convertedQuotations = await _unitOfWork.Repository<Quotation>().Query()
            .Where(q => q.ConvertedOrderId.HasValue && orderIds.Contains(q.ConvertedOrderId.Value))
            .Select(q => new { OrderId = q.ConvertedOrderId!.Value, q.QuotationNumber })
            .ToListAsync(cancellationToken);

        var quotationMap = convertedQuotations.ToDictionary(x => x.OrderId, x => x.QuotationNumber);
        foreach (var order in orders)
        {
            if (quotationMap.TryGetValue(order.Id, out var quotationNumber))
            {
                order.IsFromApprovedQuotation = true;
                order.SourceQuotationNumber = quotationNumber;
            }
        }
    }

    private static DateTime AsUtc(DateTime value) => value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();

    private static DateTime? AsUtc(DateTime? value) => value.HasValue ? AsUtc(value.Value) : null;

    private static OrderDto MapToDto(Order o) => new()
    {
        Id = o.Id,
        OrderNumber = o.OrderNumber,
        CustomerId = o.CustomerId,
        CustomerName = o.Customer?.User?.Username ?? o.Customer?.ShopName ?? "",
        ShopName = o.Customer?.ShopName,
        RepId = o.RepId,
        RepName = o.Rep?.FullName,
        OrderDate = o.OrderDate,
        RequiredDeliveryDate = o.RequiredDeliveryDate,
        ActualDeliveryDate = o.ActualDeliveryDate,
        Status = o.Status.ToString(),
        SubTotal = o.SubTotal,
        TaxAmount = o.TaxAmount,
        DiscountAmount = o.DiscountAmount,
        TotalAmount = o.TotalAmount,
        DeliveryAddress = o.DeliveryAddress,
        DeliveryNotes = o.DeliveryNotes,
        Rating = o.Rating,
        IsDeleted = o.IsDeleted,
        DeletedAt = AsUtc(o.DeletedAt),
        Items = o.OrderItems?.Select(oi => new OrderItemDto
        {
            Id = oi.Id,
            ProductId = oi.ProductId,
            ProductName = oi.ProductName,
            ProductSKU = oi.ProductSKU,
            TaxCode = oi.TaxCode,
            Quantity = oi.Quantity,
            BackorderedQuantity = oi.BackorderedQuantity,
            UnitPrice = oi.UnitPrice,
            MRP = oi.MRP,
            DiscountPercent = oi.DiscountPercent,
            TaxAmount = oi.TaxAmount,
            LineTotal = oi.LineTotal
        }).ToList() ?? [],
        CreatedAt = AsUtc(o.CreatedAt)
    };
}
