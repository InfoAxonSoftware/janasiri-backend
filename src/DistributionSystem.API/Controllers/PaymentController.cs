using System.Security.Claims;
using DistributionSystem.Application.DTOs.Common;
using DistributionSystem.Application.DTOs.Notification;
using DistributionSystem.Application.DTOs.Payment;
using DistributionSystem.Application.Services.Interfaces;
using DistributionSystem.API.Hubs;
using DistributionSystem.Infrastructure.Data.Repositories.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace DistributionSystem.API.Controllers;

/// <summary>
/// Payment management endpoints
/// </summary>
[ApiController]
[Route("api")]
[Authorize]
[Produces("application/json")]
public class PaymentController : ControllerBase
{
    private readonly IPaymentService _paymentService;
    private readonly INotificationService _notificationService;
    private readonly ICustomerService _customerService;
    private readonly IHubContext<NotificationHub> _notificationHub;
    private readonly IUnitOfWork _unitOfWork;

    public PaymentController(IPaymentService paymentService, INotificationService notificationService,
        ICustomerService customerService, IHubContext<NotificationHub> notificationHub, IUnitOfWork unitOfWork)
    {
        _paymentService = paymentService;
        _notificationService = notificationService;
        _customerService = customerService;
        _notificationHub = notificationHub;
        _unitOfWork = unitOfWork;
    }

    private Guid GetUserId() => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    // ===== ADMIN ENDPOINTS =====

    /// <summary>Get all payments (Admin)</summary>
    [HttpGet("admin/payments")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public async Task<IActionResult> GetAllPayments([FromQuery] int page = 1, [FromQuery] int pageSize = 20,
        [FromQuery] Guid? customerId = null, CancellationToken ct = default)
    {
        var result = await _paymentService.GetAllAsync(page, pageSize, customerId, ct);
        return Ok(ApiResponse<PagedResult<PaymentDto>>.SuccessResponse(result));
    }

    /// <summary>Get payment by ID (Admin)</summary>
    [HttpGet("admin/payments/{id}")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public async Task<IActionResult> GetPayment(Guid id, CancellationToken ct)
    {
        var result = await _paymentService.GetByIdAsync(id, ct);
        return Ok(ApiResponse<PaymentDto>.SuccessResponse(result));
    }

    /// <summary>Verify/Confirm a payment (Admin)</summary>
    [HttpPost("admin/payments/{id}/verify")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public async Task<IActionResult> VerifyPayment(Guid id, CancellationToken ct)
    {
        var result = await _paymentService.VerifyAsync(id, ct);

        // Notify the customer that their payment was verified
        try
        {
            var customer = await _customerService.GetByIdAsync(result.CustomerId, ct);
            if (customer?.UserId != null)
            {
                await _notificationService.SendNotificationAsync(
                    customer.UserId, Domain.Enums.NotificationType.PaymentReminder,
                    "Payment Verified", $"Your payment of {result.Amount:C} has been verified.", ct);
            }
        }
        catch { /* best-effort notification */ }

        return Ok(ApiResponse<PaymentDto>.SuccessResponse(result, "Payment verified"));
    }

    /// <summary>Get customer ledger (Admin)</summary>
    [HttpGet("admin/customers/{customerId}/ledger")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public async Task<IActionResult> GetCustomerLedger(Guid customerId, CancellationToken ct)
    {
        var result = await _paymentService.GetCustomerLedgerAsync(customerId, ct);
        return Ok(ApiResponse<CustomerLedgerDto>.SuccessResponse(result));
    }

    // ===== REP ENDPOINTS =====

    /// <summary>Record a payment collected from customer (Rep)</summary>
    [HttpPost("rep/payments")]
    [Authorize(Roles = "SalesRep")]
    public async Task<IActionResult> RecordPayment([FromBody] CreatePaymentRequest request, CancellationToken ct)
    {
        var userId = GetUserId();
        // resolve SalesRepProfile.Id from the logged-in user's UserId (rep profile id is the FK used by Payment.CollectedByRepId)
        var repProfile = await _unitOfWork.Repository<Domain.Entities.SalesRepProfile>().Query()
            .FirstOrDefaultAsync(r => r.UserId == userId, ct);
        var collectedByRepId = repProfile?.Id;

        var result = await _paymentService.CreateAsync(request, collectedByRepId, ct);

        // Notify admin about new payment recorded
        await _notificationService.SendToRoleAsync(new BroadcastNotificationRequest
        {
            Role = "Admin",
            Title = "Payment Recorded",
            Message = $"Payment of {result.Amount:C} recorded for {result.CustomerName} by rep",
            Type = "PaymentReminder"
        }, ct);

        return Ok(ApiResponse<PaymentDto>.SuccessResponse(result, "Payment recorded"));
    }

    /// <summary>Get payments collected by rep (Rep)</summary>
    [HttpGet("rep/payments")]
    [Authorize(Roles = "SalesRep")]
    public async Task<IActionResult> GetRepPayments([FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
    {
        var userId = GetUserId();
        var result = await _paymentService.GetByRepAsync(userId, page, pageSize, ct);
        return Ok(ApiResponse<PagedResult<PaymentDto>>.SuccessResponse(result));
    }

    /// <summary>Get customer outstanding balance (Rep)</summary>
    [HttpGet("rep/customers/{customerId}/balance")]
    [Authorize(Roles = "SalesRep")]
    public async Task<IActionResult> GetCustomerBalance(Guid customerId, CancellationToken ct)
    {
        var result = await _paymentService.GetCustomerLedgerAsync(customerId, ct);
        return Ok(ApiResponse<CustomerLedgerDto>.SuccessResponse(result));
    }

    // ===== CUSTOMER ENDPOINTS =====

    /// <summary>Get my payment history (Customer)</summary>
    [HttpGet("customer/payments")]
    [Authorize(Roles = "Customer")]
    public async Task<IActionResult> GetMyPayments([FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
    {
        var userId = GetUserId();
        var result = await _paymentService.GetByCustomerUserIdAsync(userId, page, pageSize, ct);
        return Ok(ApiResponse<PagedResult<PaymentDto>>.SuccessResponse(result));
    }

    /// <summary>Get my account balance (Customer)</summary>
    [HttpGet("customer/balance")]
    [Authorize(Roles = "Customer")]
    public async Task<IActionResult> GetMyBalance(CancellationToken ct)
    {
        var userId = GetUserId();
        var result = await _paymentService.GetCustomerLedgerByUserIdAsync(userId, ct);
        return Ok(ApiResponse<CustomerLedgerDto>.SuccessResponse(result));
    }
}

