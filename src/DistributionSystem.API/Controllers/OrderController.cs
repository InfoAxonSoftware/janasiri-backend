using System.Security.Claims;
using DistributionSystem.Application.DTOs.Common;
using DistributionSystem.Application.DTOs.Notification;
using DistributionSystem.Application.DTOs.Order;
using DistributionSystem.Application.Services.Interfaces;
using DistributionSystem.API.Hubs;
using DistributionSystem.Domain.Entities;
using DistributionSystem.Domain.Enums;
using DistributionSystem.Infrastructure.Data.Repositories.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace DistributionSystem.API.Controllers;

/// <summary>
/// Order management endpoints for Admin, SalesRep, and Customer roles
/// </summary>
[ApiController]
[Route("api")]
[Authorize]
[Produces("application/json")]
public class OrderController : ControllerBase
{
    private readonly IOrderService _orderService;
    private readonly ICustomerService _customerService;
    private readonly INotificationService _notificationService;
    private readonly IEmailService _emailService;
    private readonly IHubContext<OrderTrackingHub> _orderHub;
    private readonly IHubContext<NotificationHub> _notificationHub;
    private readonly IUnitOfWork _unitOfWork;

    public OrderController(IOrderService orderService, ICustomerService customerService,
        INotificationService notificationService, IEmailService emailService,
        IHubContext<OrderTrackingHub> orderHub, IHubContext<NotificationHub> notificationHub,
        IUnitOfWork unitOfWork)
    {
        _orderService = orderService;
        _customerService = customerService;
        _notificationService = notificationService;
        _emailService = emailService;
        _orderHub = orderHub;
        _notificationHub = notificationHub;
        _unitOfWork = unitOfWork;
    }

    private Guid GetUserId() => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private string GetUserRole() => User.FindFirstValue(ClaimTypes.Role) ?? "";

    private async Task<Guid> ResolveCoordinatorProfileIdAsync(Guid userId, CancellationToken ct)
    {
        var coordinator = await _unitOfWork.Repository<CoordinatorProfile>().Query()
            .FirstOrDefaultAsync(c => c.UserId == userId, ct)
            ?? throw new KeyNotFoundException("Coordinator profile not found");

        return coordinator.Id;
    }

    private async Task<bool> IsOrderVisibleToCoordinatorAsync(Guid orderId, Guid coordinatorProfileId, CancellationToken ct)
    {
        return await _unitOfWork.Repository<Order>().Query()
            .Include(o => o.Customer)
            .Include(o => o.Rep).ThenInclude(r => r!.Coordinators)
            .AnyAsync(o => o.Id == orderId &&
                           (o.Customer.AssignedCoordinatorId == coordinatorProfileId
                            || (o.RepId.HasValue && o.Rep != null && o.Rep.Coordinators.Any(rc => rc.CoordinatorId == coordinatorProfileId))), ct);
    }

    private async Task PublishOrderStatusChangedAsync(OrderDto order, string status, string? reason, CancellationToken ct)
    {
        var payload = new { OrderId = order.Id, Status = status, Reason = reason };

        // Keep per-order updates for detail views that may subscribe to this group.
        await _orderHub.Clients.Group($"order_{order.Id}").SendAsync("OrderStatusChanged", payload, ct);

        // Admin list/detail screens should always refresh on any order lifecycle change.
        await _notificationHub.Clients.Group("role_Admin").SendAsync("OrderStatusChanged", payload, ct);

        var customerInfo = await _unitOfWork.Repository<CustomerProfile>().Query()
            .AsNoTracking()
            .Where(c => c.Id == order.CustomerId)
            .Select(c => new { c.UserId, c.AssignedCoordinatorId })
            .FirstOrDefaultAsync(ct);

        if (customerInfo?.UserId != null)
        {
            await _notificationHub.Clients.Group($"user_{customerInfo.UserId}").SendAsync("OrderStatusChanged", payload, ct);
        }

        if (order.RepId.HasValue)
        {
            var repUserId = await _unitOfWork.Repository<SalesRepProfile>().Query()
                .AsNoTracking()
                .Where(r => r.Id == order.RepId.Value)
                .Select(r => (Guid?)r.UserId)
                .FirstOrDefaultAsync(ct);

            if (repUserId.HasValue)
            {
                await _notificationHub.Clients.Group($"user_{repUserId.Value}").SendAsync("OrderStatusChanged", payload, ct);
            }
        }

        if (customerInfo?.AssignedCoordinatorId.HasValue == true)
        {
            var coordinatorUserId = await _unitOfWork.Repository<CoordinatorProfile>().Query()
                .AsNoTracking()
                .Where(c => c.Id == customerInfo.AssignedCoordinatorId.Value)
                .Select(c => (Guid?)c.UserId)
                .FirstOrDefaultAsync(ct);

            if (coordinatorUserId.HasValue)
            {
                await _notificationHub.Clients.Group($"user_{coordinatorUserId.Value}").SendAsync("OrderStatusChanged", payload, ct);
            }
        }
    }

    // ===== ADMIN ENDPOINTS =====

    /// <summary>Get all orders with filtering (Admin)</summary>
    [HttpGet("admin/orders")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public async Task<IActionResult> AdminGetOrders([FromQuery] OrderFilterRequest filter, CancellationToken ct)
    {
        var result = await _orderService.GetAllAsync(filter, ct);
        return Ok(ApiResponse<PagedResult<OrderDto>>.SuccessResponse(result));
    }

    [HttpGet("admin/orders/unified")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public async Task<IActionResult> AdminGetUnifiedOrders([FromQuery] UnifiedOrderFilterRequest filter, CancellationToken ct)
    {
        var result = await _orderService.GetUnifiedAdminAsync(filter, false, ct);
        return Ok(ApiResponse<PagedResult<UnifiedOrderDto>>.SuccessResponse(result));
    }

    /// <summary>Get order details (Admin)</summary>
    [HttpGet("admin/orders/{id}")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public async Task<IActionResult> AdminGetOrder(Guid id, CancellationToken ct)
    {
        var result = await _orderService.GetByIdAsync(id, ct);
        return Ok(ApiResponse<OrderDto>.SuccessResponse(result));
    }

    /// <summary>Approve an order (Admin)</summary>
    [HttpPost("admin/orders/{id}/approve")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public async Task<IActionResult> ApproveOrder(Guid id, CancellationToken ct)
    {
        var approvedBy = GetUserId();
        var result = await _orderService.ApproveAsync(id, approvedBy, ct);
        await PublishOrderStatusChangedAsync(result, "Approved", null, ct);
        // Persist notification to customer so they see it even if offline
        if (result.CustomerId != Guid.Empty)
        {
            var customer = await _customerService.GetByIdAsync(result.CustomerId, ct);
            if (customer?.UserId != null)
            {
                await _notificationService.SendNotificationAsync(
                    customer.UserId, Domain.Enums.NotificationType.OrderStatusUpdate,
                    "Order Approved", $"Your order #{result.OrderNumber} has been approved.", ct);
            }
        }
        return Ok(ApiResponse<OrderDto>.SuccessResponse(result, "Order approved"));
    }

    /// <summary>Reject an order (Admin)</summary>
    [HttpPost("admin/orders/{id}/reject")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public async Task<IActionResult> RejectOrder(Guid id, [FromBody] string? reason, CancellationToken ct)
    {
        var result = await _orderService.RejectAsync(id, reason ?? "", ct);
        await PublishOrderStatusChangedAsync(result, "Rejected", reason, ct);
        // Persist notification to customer
        if (result.CustomerId != Guid.Empty)
        {
            var customer = await _customerService.GetByIdAsync(result.CustomerId, ct);
            if (customer?.UserId != null)
            {
                await _notificationService.SendNotificationAsync(
                    customer.UserId, Domain.Enums.NotificationType.OrderStatusUpdate,
                    "Order Rejected", $"Your order #{result.OrderNumber} was rejected.{(string.IsNullOrEmpty(reason) ? "" : $" Reason: {reason}")}", ct);
            }
        }
        return Ok(ApiResponse<OrderDto>.SuccessResponse(result, "Order rejected"));
    }

    /// <summary>Soft-delete (trash) an order (Admin)</summary>
    [HttpDelete("admin/orders/{id}")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public async Task<IActionResult> AdminDeleteOrder(Guid id, CancellationToken ct)
    {
        await _orderService.AdminSoftDeleteAsync(id, GetUserId().ToString(), ct);
        return Ok(ApiResponse<string>.SuccessResponse("Order moved to trash"));
    }

    /// <summary>Get trash (Admin)</summary>
    [HttpGet("admin/orders/trash")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public async Task<IActionResult> AdminGetTrash([FromQuery] UnifiedOrderFilterRequest filter, CancellationToken ct = default)
    {
        var result = await _orderService.GetUnifiedAdminAsync(filter, true, ct);
        return Ok(ApiResponse<PagedResult<UnifiedOrderDto>>.SuccessResponse(result));
    }

    /// <summary>Restore a trashed order (Admin)</summary>
    [HttpPost("admin/orders/{id}/restore")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public async Task<IActionResult> AdminRestoreOrder(Guid id, CancellationToken ct)
    {
        await _orderService.AdminRestoreAsync(id, ct);
        return Ok(ApiResponse<string>.SuccessResponse("Order restored"));
    }

    [HttpPost("admin/orders/trash/restore")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public async Task<IActionResult> AdminRestoreTrash([FromBody] OrderTrashItemsRequest request, CancellationToken ct)
    {
        await _orderService.AdminRestoreTrashAsync(request.Items, ct);
        return Ok(ApiResponse<string>.SuccessResponse("Restored."));
    }

    [HttpPost("admin/orders/trash/permanent")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public async Task<IActionResult> AdminPurgeTrash([FromBody] OrderTrashItemsRequest request, CancellationToken ct)
    {
        await _orderService.AdminPurgeTrashAsync(request.Items, ct);
        return Ok(ApiResponse<string>.SuccessResponse("Removed from Admin view."));
    }

    [HttpDelete("admin/orders/trash")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public async Task<IActionResult> AdminEmptyTrash(CancellationToken ct)
    {
        await _orderService.AdminEmptyTrashAsync(ct);
        return Ok(ApiResponse<string>.SuccessResponse("Admin trash emptied."));
    }

    /// <summary>Update order status (Admin)</summary>
    [HttpPut("admin/orders/{id}/status")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public async Task<IActionResult> UpdateOrderStatus(Guid id, [FromBody] UpdateOrderStatusRequest request, CancellationToken ct)
    {
        var result = await _orderService.UpdateStatusAsync(id, request, ct);
        await PublishOrderStatusChangedAsync(result, request.Status.ToString(), request.Reason, ct);
        // Persist notification to customer for status updates
        if (result.CustomerId != Guid.Empty)
        {
            var customer = await _customerService.GetByIdAsync(result.CustomerId, ct);
            if (customer?.UserId != null)
            {
                await _notificationService.SendNotificationAsync(
                    customer.UserId, Domain.Enums.NotificationType.OrderStatusUpdate,
                    "Order Status Updated", $"Your order #{result.OrderNumber} is now {request.Status}.", ct);
            }
        }
        return Ok(ApiResponse<OrderDto>.SuccessResponse(result, "Order status updated"));
    }

    // ===== REP ENDPOINTS =====

    /// <summary>Create order for a customer (Rep)</summary>
    [HttpPost("rep/orders")]
    [Authorize(Roles = "SalesRep")]
    public async Task<IActionResult> RepCreateOrder([FromBody] CreateOrderRequest request, CancellationToken ct)
    {
        var userId = GetUserId();
        var result = await _orderService.CreateAsync(request, userId, ct);

        // get rep name for human-friendly message/metadata
        var repProfile = await _unitOfWork.Repository<SalesRepProfile>()
            .Query()
            .Include(r => r.Coordinators).ThenInclude(rc => rc.Coordinator).ThenInclude(c => c.User)
            .FirstOrDefaultAsync(r => r.UserId == userId, ct);
        var actorName = repProfile?.FullName ?? repProfile?.EmployeeCode ?? "sales rep";

        // Persist DB notification for all admins + push real-time
        var customerName = result.CustomerName;
        var meta = JsonSerializer.Serialize(new { orderId = result.Id, actorName, customerName });
        await _notificationService.SendToRoleAsync(new BroadcastNotificationRequest
        {
            Role = "Admin",
            Title = "New Order",
            Message = $"New order #{result.OrderNumber} for {customerName} placed by {actorName}",
            Type = "NewOrder",
            Metadata = meta
        }, ct);
        await _notificationHub.Clients.Group("role_Admin").SendAsync("NewOrder", new { result.Id, result.OrderNumber, actorName, customerName }, ct);

        // Notify each coordinator assigned to this rep (DB notification + real-time)
        if (repProfile?.Coordinators != null)
        {
            foreach (var rc in repProfile.Coordinators)
            {
                var coordUserId = rc.Coordinator?.UserId;
                if (coordUserId.HasValue)
                {
                    await _notificationService.SendNotificationAsync(
                        coordUserId.Value,
                        Domain.Enums.NotificationType.NewOrder,
                        "New Order",
                        $"New order #{result.OrderNumber} for {customerName} placed by {actorName}.",
                        ct);
                    await _notificationHub.Clients.Group($"user_{coordUserId.Value}")
                        .SendAsync("NewOrder", new { result.Id, result.OrderNumber, actorName, customerName, channel = "coordinator" }, ct);
                }
            }
        }

        // Send email notification (fire-and-forget, won't block response)
        _ = _emailService.SendOrderNotificationToAdminAsync(result.OrderNumber, "Sales Rep", result.TotalAmount, ct);

        return CreatedAtAction(nameof(AdminGetOrder), new { id = result.Id }, ApiResponse<OrderDto>.SuccessResponse(result, "Order created"));
    }

    /// <summary>Get rep's orders (Rep)</summary>
    [HttpGet("rep/orders")]
    [Authorize(Roles = "SalesRep")]
    public async Task<IActionResult> RepGetOrders([FromQuery] OrderFilterRequest filter, CancellationToken ct)
    {
        var userId = GetUserId();
        var repProfile = await _unitOfWork.Repository<SalesRepProfile>().Query().FirstOrDefaultAsync(r => r.UserId == userId, ct);
        filter.RepId = repProfile?.Id; // use SalesRepProfile.Id (nullable) — if not found, no results will be returned

        var result = await _orderService.GetAllAsync(filter, ct);
        return Ok(ApiResponse<PagedResult<OrderDto>>.SuccessResponse(result));
    }

    [HttpGet("rep/orders/unified")]
    [Authorize(Roles = "SalesRep")]
    public async Task<IActionResult> RepGetUnifiedOrders([FromQuery] UnifiedOrderFilterRequest filter, CancellationToken ct)
    {
        var result = await _orderService.GetUnifiedRepAsync(GetUserId(), filter, false, ct);
        return Ok(ApiResponse<PagedResult<UnifiedOrderDto>>.SuccessResponse(result));
    }

    /// <summary>Get rep's specific order (Rep)</summary>
    [HttpGet("rep/orders/{id}")]
    [Authorize(Roles = "SalesRep")]
    public async Task<IActionResult> RepGetOrder(Guid id, CancellationToken ct)
    {
        var result = await _orderService.GetByIdAsync(id, ct);
        return Ok(ApiResponse<OrderDto>.SuccessResponse(result));
    }

    // ===== COORDINATOR ENDPOINTS =====

    /// <summary>Get coordinator-visible orders</summary>
    [HttpGet("coordinator/orders")]
    [Authorize(Roles = "SalesCoordinator")]
    public async Task<IActionResult> CoordinatorGetOrders([FromQuery] OrderFilterRequest filter, CancellationToken ct)
    {
        var result = await _orderService.GetForCoordinatorAsync(GetUserId(), filter, ct);
        return Ok(ApiResponse<PagedResult<OrderDto>>.SuccessResponse(result));
    }

    [HttpGet("coordinator/orders/unified")]
    [Authorize(Roles = "SalesCoordinator")]
    public async Task<IActionResult> CoordinatorGetUnifiedOrders([FromQuery] UnifiedOrderFilterRequest filter, CancellationToken ct)
    {
        var result = await _orderService.GetUnifiedCoordinatorAsync(GetUserId(), filter, false, ct);
        return Ok(ApiResponse<PagedResult<UnifiedOrderDto>>.SuccessResponse(result));
    }

    /// <summary>Get a coordinator-visible order by id</summary>
    [HttpGet("coordinator/orders/{id}")]
    [Authorize(Roles = "SalesCoordinator")]
    public async Task<IActionResult> CoordinatorGetOrder(Guid id, CancellationToken ct)
    {
        var userId = GetUserId();
        var coordinatorId = await ResolveCoordinatorProfileIdAsync(userId, ct);
        var canView = await IsOrderVisibleToCoordinatorAsync(id, coordinatorId, ct);
        if (!canView)
            return Forbid();

        var result = await _orderService.GetByIdAsync(id, ct);
        return Ok(ApiResponse<OrderDto>.SuccessResponse(result));
    }

    /// <summary>Approve an order (Coordinator)</summary>
    [HttpPost("coordinator/orders/{id}/approve")]
    [Authorize(Roles = "SalesCoordinator")]
    public async Task<IActionResult> CoordinatorApproveOrder(Guid id, CancellationToken ct)
    {
        var userId = GetUserId();
        var coordinatorId = await ResolveCoordinatorProfileIdAsync(userId, ct);
        var canApprove = await IsOrderVisibleToCoordinatorAsync(id, coordinatorId, ct);
        if (!canApprove)
            return Forbid();

        var result = await _orderService.ApproveAsync(id, userId, ct);
        await PublishOrderStatusChangedAsync(result, "Approved", null, ct);

        if (result.CustomerId != Guid.Empty)
        {
            var customer = await _customerService.GetByIdAsync(result.CustomerId, ct);
            if (customer?.UserId != null)
            {
                await _notificationService.SendNotificationAsync(
                    customer.UserId, Domain.Enums.NotificationType.OrderStatusUpdate,
                    "Order Approved", $"Your order #{result.OrderNumber} has been approved by coordinator.", ct);
            }
        }

        return Ok(ApiResponse<OrderDto>.SuccessResponse(result, "Order approved"));
    }

    /// <summary>Reject an order (Coordinator)</summary>
    [HttpPost("coordinator/orders/{id}/reject")]
    [Authorize(Roles = "SalesCoordinator")]
    public async Task<IActionResult> CoordinatorRejectOrder(Guid id, [FromBody] string? reason, CancellationToken ct)
    {
        var userId = GetUserId();
        var coordinatorId = await ResolveCoordinatorProfileIdAsync(userId, ct);
        var canReject = await IsOrderVisibleToCoordinatorAsync(id, coordinatorId, ct);
        if (!canReject)
            return Forbid();

        var result = await _orderService.RejectAsync(id, reason ?? "", ct);
        await PublishOrderStatusChangedAsync(result, "Rejected", reason, ct);
        return Ok(ApiResponse<OrderDto>.SuccessResponse(result, "Order rejected"));
    }

    /// <summary>Update order status (Coordinator)</summary>
    [HttpPut("coordinator/orders/{id}/status")]
    [Authorize(Roles = "SalesCoordinator")]
    public async Task<IActionResult> CoordinatorUpdateOrderStatus(Guid id, [FromBody] UpdateOrderStatusRequest request, CancellationToken ct)
    {
        var userId = GetUserId();
        var coordinatorId = await ResolveCoordinatorProfileIdAsync(userId, ct);
        var canUpdate = await IsOrderVisibleToCoordinatorAsync(id, coordinatorId, ct);
        if (!canUpdate)
            return Forbid();

        var result = await _orderService.UpdateStatusAsync(id, request, ct);
        await PublishOrderStatusChangedAsync(result, request.Status.ToString(), request.Reason, ct);
        return Ok(ApiResponse<OrderDto>.SuccessResponse(result, "Order status updated"));
    }

    /// <summary>Cancel an order (Rep)</summary>
    [HttpPost("rep/orders/{id}/cancel")]
    [Authorize(Roles = "SalesRep")]
    public async Task<IActionResult> RepCancelOrder(Guid id, [FromBody] string? reason, CancellationToken ct)
    {
        var result = await _orderService.CancelAsync(id, reason ?? "", ct);
        await PublishOrderStatusChangedAsync(result, "Cancelled", reason, ct);
        return Ok(ApiResponse<OrderDto>.SuccessResponse(result, "Order cancelled"));
    }

    // ===== CUSTOMER ENDPOINTS =====

    /// <summary>Place order (Customer)</summary>
    [HttpPost("customer/orders")]
    [Authorize(Roles = "Customer")]
    public async Task<IActionResult> CustomerCreateOrder([FromBody] CreateOrderRequest request, CancellationToken ct)
    {
        var userId = GetUserId();
        // Get the customer profile ID from the logged-in user
        var customer = await _customerService.GetByUserIdAsync(userId, ct);
        request.CustomerId = customer.Id;
        
        var result = await _orderService.CreateAsync(request, null, ct);

        var customerEntity = await _unitOfWork.Repository<CustomerProfile>().Query()
            .Include(c => c.AssignedRep).ThenInclude(r => r!.User)
            .Include(c => c.AssignedCoordinator).ThenInclude(c => c!.User)
            .FirstOrDefaultAsync(c => c.Id == customer.Id, ct);

        // Persist DB notification for all admins + push real-time
        var actorName = customer.ShopName ?? "customer";
        var meta = JsonSerializer.Serialize(new { orderId = result.Id, actorName });
        await _notificationService.SendToRoleAsync(new BroadcastNotificationRequest
        {
            Role = "Admin",
            Title = "New Order",
            Message = $"New order #{result.OrderNumber} placed by {actorName}",
            Type = "NewOrder",
            Metadata = meta
        }, ct);
        await _notificationHub.Clients.Group("role_Admin").SendAsync("NewOrder", new { result.Id, result.OrderNumber, actorName }, ct);

        // Notify assigned sales rep (if any)
        var repUserId = customerEntity?.AssignedRep?.UserId;
        if (repUserId.HasValue)
        {
            await _notificationService.SendNotificationAsync(
                repUserId.Value,
                Domain.Enums.NotificationType.NewOrder,
                "New Customer Order",
                $"Customer {actorName} placed order #{result.OrderNumber}.",
                ct);

            await _notificationHub.Clients.Group($"user_{repUserId.Value}")
                .SendAsync("NewOrder", new { result.Id, result.OrderNumber, actorName, channel = "rep" }, ct);
        }

        // Notify assigned coordinator (if any)
        var coordinatorUserId = customerEntity?.AssignedCoordinator?.UserId;
        if (coordinatorUserId.HasValue)
        {
            await _notificationService.SendNotificationAsync(
                coordinatorUserId.Value,
                Domain.Enums.NotificationType.NewOrder,
                "New Customer Order",
                $"Customer {actorName} placed order #{result.OrderNumber}.",
                ct);

            await _notificationHub.Clients.Group($"user_{coordinatorUserId.Value}")
                .SendAsync("NewOrder", new { result.Id, result.OrderNumber, actorName, channel = "coordinator" }, ct);
        }

        // Send email notification (fire-and-forget, won't block response)
        _ = _emailService.SendOrderNotificationToAdminAsync(result.OrderNumber, "Customer", result.TotalAmount, ct);

        return CreatedAtAction(nameof(AdminGetOrder), new { id = result.Id }, ApiResponse<OrderDto>.SuccessResponse(result, "Order placed"));
    }

    /// <summary>Get customer's order history (Customer)</summary>
    [HttpGet("customer/orders")]
    [Authorize(Roles = "Customer")]
    public async Task<IActionResult> CustomerGetOrders([FromQuery] OrderFilterRequest filter, CancellationToken ct)
    {
        var userId = GetUserId();
        var customer = await _customerService.GetByUserIdAsync(userId, ct);
        filter.CustomerId = customer.Id;
        var result = await _orderService.GetAllAsync(filter, ct);
        return Ok(ApiResponse<PagedResult<OrderDto>>.SuccessResponse(result));
    }

    /// <summary>Get customer's specific order (Customer)</summary>
    [HttpGet("customer/orders/{id}")]
    [Authorize(Roles = "Customer")]
    public async Task<IActionResult> CustomerGetOrder(Guid id, CancellationToken ct)
    {
        var result = await _orderService.GetByIdAsync(id, ct);
        return Ok(ApiResponse<OrderDto>.SuccessResponse(result));
    }

    /// <summary>Track order in real-time (Customer)</summary>
    [HttpGet("customer/orders/{id}/track")]
    [Authorize(Roles = "Customer")]
    public async Task<IActionResult> TrackOrder(Guid id, CancellationToken ct)
    {
        var result = await _orderService.GetByIdAsync(id, ct);
        return Ok(ApiResponse<object>.SuccessResponse(new
        {
            result.Id,
            result.OrderNumber,
            result.Status,
            result.CreatedAt,
            result.TotalAmount,
            result.RequiredDeliveryDate,
            result.ActualDeliveryDate
        }));
    }

    /// <summary>Cancel an order (Customer)</summary>
    [HttpPost("customer/orders/{id}/cancel")]
    [Authorize(Roles = "Customer")]
    public async Task<IActionResult> CustomerCancelOrder(Guid id, [FromBody] string? reason, CancellationToken ct)
    {
        var result = await _orderService.CancelAsync(id, reason ?? "", ct);
        await PublishOrderStatusChangedAsync(result, "Cancelled", reason, ct);
        return Ok(ApiResponse<OrderDto>.SuccessResponse(result, "Order cancelled"));
    }

    /// <summary>Rate an order (Customer)</summary>
    [HttpPost("customer/orders/{id}/rate")]
    [Authorize(Roles = "Customer")]
    public async Task<IActionResult> RateOrder(Guid id, [FromBody] RateOrderRequest request, CancellationToken ct)
    {
        await _orderService.RateOrderAsync(id, request, ct);
        return Ok(ApiResponse<string>.SuccessResponse("Order rated"));
    }

    /// <summary>Reorder a previous order (Customer)</summary>
    [HttpPost("customer/orders/{id}/reorder")]
    [Authorize(Roles = "Customer")]
    public async Task<IActionResult> ReorderPrevious(Guid id, CancellationToken ct)
    {
        var userId = GetUserId();
        var result = await _orderService.ReorderAsync(id, userId, ct);
        return Ok(ApiResponse<OrderDto>.SuccessResponse(result, "Reorder created"));
    }

    // ===== REP TRASH =====

    /// <summary>Move rep's order to trash</summary>
    [HttpDelete("rep/orders/{id}")]
    [Authorize(Roles = "SalesRep")]
    public async Task<IActionResult> RepDeleteOrder(Guid id, CancellationToken ct)
    {
        await _orderService.RepSoftDeleteAsync(id, GetUserId(), ct);
        return Ok(ApiResponse<string>.SuccessResponse("Order moved to trash"));
    }

    /// <summary>Get rep's trash</summary>
    [HttpGet("rep/orders/trash")]
    [Authorize(Roles = "SalesRep")]
    public async Task<IActionResult> RepGetOrderTrash([FromQuery] UnifiedOrderFilterRequest filter, CancellationToken ct = default)
    {
        var result = await _orderService.GetUnifiedRepAsync(GetUserId(), filter, true, ct);
        return Ok(ApiResponse<PagedResult<UnifiedOrderDto>>.SuccessResponse(result));
    }

    /// <summary>Restore rep's order from trash</summary>
    [HttpPost("rep/orders/{id}/restore")]
    [Authorize(Roles = "SalesRep")]
    public async Task<IActionResult> RepRestoreOrder(Guid id, CancellationToken ct)
    {
        await _orderService.RepRestoreAsync(id, GetUserId(), ct);
        return Ok(ApiResponse<string>.SuccessResponse("Order restored"));
    }

    [HttpPost("rep/orders/trash/restore")]
    [Authorize(Roles = "SalesRep")]
    public async Task<IActionResult> RepRestoreTrash([FromBody] OrderTrashItemsRequest request, CancellationToken ct)
    {
        await _orderService.RepRestoreTrashAsync(GetUserId(), request.Items, ct);
        return Ok(ApiResponse<string>.SuccessResponse(nameof(RepRestoreTrash)));
    }

    [HttpPost("rep/orders/trash/permanent")]
    [Authorize(Roles = "SalesRep")]
    public async Task<IActionResult> RepPurgeTrash([FromBody] OrderTrashItemsRequest request, CancellationToken ct)
    {
        await _orderService.RepPurgeTrashAsync(GetUserId(), request.Items, ct);
        return Ok(ApiResponse<string>.SuccessResponse(nameof(RepPurgeTrash)));
    }

    [HttpDelete("rep/orders/trash")]
    [Authorize(Roles = "SalesRep")]
    public async Task<IActionResult> RepEmptyTrash(CancellationToken ct)
    {
        await _orderService.RepEmptyTrashAsync(GetUserId(), ct);
        return Ok(ApiResponse<string>.SuccessResponse(nameof(RepEmptyTrash)));
    }

    // ===== COORDINATOR TRASH =====

    /// <summary>Move coordinator-visible order to coordinator's trash</summary>
    [HttpDelete("coordinator/orders/{id}")]
    [Authorize(Roles = "SalesCoordinator")]
    public async Task<IActionResult> CoordinatorDeleteOrder(Guid id, CancellationToken ct)
    {
        await _orderService.CoordinatorSoftDeleteAsync(id, GetUserId(), ct);
        return Ok(ApiResponse<string>.SuccessResponse("Order moved to trash"));
    }

    /// <summary>Get coordinator's trash</summary>
    [HttpGet("coordinator/orders/trash")]
    [Authorize(Roles = "SalesCoordinator")]
    public async Task<IActionResult> CoordinatorGetOrderTrash([FromQuery] UnifiedOrderFilterRequest filter, CancellationToken ct = default)
    {
        var result = await _orderService.GetUnifiedCoordinatorAsync(GetUserId(), filter, true, ct);
        return Ok(ApiResponse<PagedResult<UnifiedOrderDto>>.SuccessResponse(result));
    }

    /// <summary>Restore coordinator's order from trash</summary>
    [HttpPost("coordinator/orders/{id}/restore")]
    [Authorize(Roles = "SalesCoordinator")]
    public async Task<IActionResult> CoordinatorRestoreOrder(Guid id, CancellationToken ct)
    {
        await _orderService.CoordinatorRestoreAsync(id, GetUserId(), ct);
        return Ok(ApiResponse<string>.SuccessResponse("Order restored"));
    }

    [HttpPost("coordinator/orders/trash/restore")]
    [Authorize(Roles = "SalesCoordinator")]
    public async Task<IActionResult> CoordinatorRestoreTrash([FromBody] OrderTrashItemsRequest request, CancellationToken ct)
    {
        await _orderService.CoordinatorRestoreTrashAsync(GetUserId(), request.Items, ct);
        return Ok(ApiResponse<string>.SuccessResponse(nameof(CoordinatorRestoreTrash)));
    }

    [HttpPost("coordinator/orders/trash/permanent")]
    [Authorize(Roles = "SalesCoordinator")]
    public async Task<IActionResult> CoordinatorPurgeTrash([FromBody] OrderTrashItemsRequest request, CancellationToken ct)
    {
        await _orderService.CoordinatorPurgeTrashAsync(GetUserId(), request.Items, ct);
        return Ok(ApiResponse<string>.SuccessResponse(nameof(CoordinatorPurgeTrash)));
    }

    [HttpDelete("coordinator/orders/trash")]
    [Authorize(Roles = "SalesCoordinator")]
    public async Task<IActionResult> CoordinatorEmptyTrash(CancellationToken ct)
    {
        await _orderService.CoordinatorEmptyTrashAsync(GetUserId(), ct);
        return Ok(ApiResponse<string>.SuccessResponse(nameof(CoordinatorEmptyTrash)));
    }

    // ===== CUSTOMER TRASH =====

    /// <summary>Move customer's order to trash</summary>
    [HttpDelete("customer/orders/{id}")]
    [Authorize(Roles = "Customer")]
    public async Task<IActionResult> CustomerDeleteOrder(Guid id, CancellationToken ct)
    {
        await _orderService.CustomerSoftDeleteAsync(id, GetUserId(), ct);
        return Ok(ApiResponse<string>.SuccessResponse("Order moved to trash"));
    }

    /// <summary>Get customer's order trash</summary>
    [HttpGet("customer/orders/trash")]
    [Authorize(Roles = "Customer")]
    public async Task<IActionResult> CustomerGetOrderTrash([FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
    {
        var result = await _orderService.CustomerGetTrashAsync(GetUserId(), page, pageSize, ct);
        return Ok(ApiResponse<PagedResult<OrderDto>>.SuccessResponse(result));
    }

    /// <summary>Restore customer's order from trash</summary>
    [HttpPost("customer/orders/{id}/restore")]
    [Authorize(Roles = "Customer")]
    public async Task<IActionResult> CustomerRestoreOrder(Guid id, CancellationToken ct)
    {
        await _orderService.CustomerRestoreAsync(id, GetUserId(), ct);
        return Ok(ApiResponse<string>.SuccessResponse("Order restored"));
    }
}

