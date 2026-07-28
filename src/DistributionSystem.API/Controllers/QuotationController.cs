using System.Security.Claims;
using DistributionSystem.Application.DTOs.Common;
using DistributionSystem.Application.DTOs.Quotation;
using DistributionSystem.Application.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DistributionSystem.API.Controllers;

/// <summary>
/// Quotation workflow endpoints
/// </summary>
[ApiController]
[Route("api")]
[Authorize]
[Produces("application/json")]
public class QuotationController : ControllerBase
{
    private readonly IQuotationService _quotationService;

    public QuotationController(IQuotationService quotationService)
    {
        _quotationService = quotationService;
    }

    private Guid GetUserId() => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    // ===== CUSTOMER ENDPOINTS =====

    /// <summary>Request a quotation (Customer)</summary>
    [HttpPost("customer/quotations")]
    [Authorize(Roles = "Customer")]
    public async Task<IActionResult> CustomerCreateQuotation([FromBody] CreateQuotationRequest request, CancellationToken ct)
    {
        var result = await _quotationService.CustomerCreateQuotationAsync(GetUserId(), request, ct);
        return CreatedAtAction(nameof(CustomerGetQuotation), new { id = result.Id }, ApiResponse<QuotationDto>.SuccessResponse(result, "Quotation submitted"));
    }

    /// <summary>Get customer's quotations</summary>
    [HttpGet("customer/quotations")]
    [Authorize(Roles = "Customer")]
    public async Task<IActionResult> CustomerGetQuotations([FromQuery] int page = 1, [FromQuery] int pageSize = 20,
        [FromQuery] string? status = null, CancellationToken ct = default)
    {
        var result = await _quotationService.CustomerGetQuotationsAsync(GetUserId(), page, pageSize, status, ct);
        return Ok(ApiResponse<PagedResult<QuotationDto>>.SuccessResponse(result));
    }

    /// <summary>Get quotation by ID (Customer)</summary>
    [HttpGet("customer/quotations/{id}")]
    [Authorize(Roles = "Customer")]
    public async Task<IActionResult> CustomerGetQuotation(Guid id, CancellationToken ct)
    {
        var result = await _quotationService.CustomerGetQuotationByIdAsync(GetUserId(), id, ct);
        return Ok(ApiResponse<QuotationDto>.SuccessResponse(result));
    }

    /// <summary>Convert approved quotation to order (Customer)</summary>
    [HttpPost("customer/quotations/{id}/convert")]
    [Authorize(Roles = "Customer")]
    public async Task<IActionResult> ConvertToOrder(Guid id, [FromBody] ConvertQuotationToOrderRequest request, CancellationToken ct)
    {
        var orderId = await _quotationService.ConvertQuotationToOrderAsync(GetUserId(), id, request, ct);
        return Ok(ApiResponse<object>.SuccessResponse(new { orderId }, "Quotation converted to order"));
    }

    /// <summary>Cancel pending quotation (Customer)</summary>
    [HttpPost("customer/quotations/{id}/cancel")]
    [Authorize(Roles = "Customer")]
    public async Task<IActionResult> CustomerCancelQuotation(Guid id, [FromBody] CancelQuotationRequest request, CancellationToken ct)
    {
        var result = await _quotationService.CustomerCancelQuotationAsync(GetUserId(), id, request, ct);
        return Ok(ApiResponse<QuotationDto>.SuccessResponse(result, "Quotation cancelled"));
    }

    // ===== REP ENDPOINTS =====

    /// <summary>Generate a quotation for customer (Rep)</summary>
    [HttpPost("rep/quotations")]
    [Authorize(Roles = "SalesRep")]
    public async Task<IActionResult> RepCreateQuotation([FromBody] CreateQuotationRequest request, CancellationToken ct)
    {
        var result = await _quotationService.RepCreateQuotationAsync(GetUserId(), request, ct);
        return CreatedAtAction(nameof(RepGetQuotation), new { id = result.Id }, ApiResponse<QuotationDto>.SuccessResponse(result, "Quotation submitted"));
    }

    /// <summary>Get rep's quotations</summary>
    [HttpGet("rep/quotations")]
    [Authorize(Roles = "SalesRep")]
    public async Task<IActionResult> RepGetQuotations([FromQuery] int page = 1, [FromQuery] int pageSize = 20,
        [FromQuery] string? status = null, CancellationToken ct = default)
    {
        var result = await _quotationService.RepGetQuotationsAsync(GetUserId(), page, pageSize, status, ct);
        return Ok(ApiResponse<PagedResult<QuotationDto>>.SuccessResponse(result));
    }

    /// <summary>Get quotation by ID (Rep)</summary>
    [HttpGet("rep/quotations/{id}")]
    [Authorize(Roles = "SalesRep")]
    public async Task<IActionResult> RepGetQuotation(Guid id, CancellationToken ct)
    {
        var result = await _quotationService.RepGetQuotationByIdAsync(GetUserId(), id, ct);
        return Ok(ApiResponse<QuotationDto>.SuccessResponse(result));
    }

    // ===== COORDINATOR ENDPOINTS =====

    /// <summary>Get quotations for review (Coordinator)</summary>
    [HttpGet("coordinator/quotations")]
    [Authorize(Roles = "SalesCoordinator")]
    public async Task<IActionResult> CoordinatorGetQuotations([FromQuery] int page = 1, [FromQuery] int pageSize = 20,
        [FromQuery] string? status = null, CancellationToken ct = default)
    {
        var result = await _quotationService.CoordinatorGetQuotationsAsync(GetUserId(), page, pageSize, status, ct);
        return Ok(ApiResponse<PagedResult<QuotationDto>>.SuccessResponse(result));
    }

    /// <summary>Get quotation by ID (Coordinator)</summary>
    [HttpGet("coordinator/quotations/{id}")]
    [Authorize(Roles = "SalesCoordinator")]
    public async Task<IActionResult> CoordinatorGetQuotation(Guid id, CancellationToken ct)
    {
        var result = await _quotationService.CoordinatorGetQuotationByIdAsync(GetUserId(), id, ct);
        return Ok(ApiResponse<QuotationDto>.SuccessResponse(result));
    }

    /// <summary>Approve a quotation (Coordinator)</summary>
    [HttpPost("coordinator/quotations/{id}/approve")]
    [Authorize(Roles = "SalesCoordinator")]
    public async Task<IActionResult> ApproveQuotation(Guid id, [FromBody] ApproveQuotationRequest request, CancellationToken ct)
    {
        var result = await _quotationService.ApproveQuotationAsync(GetUserId(), id, request, ct);
        return Ok(ApiResponse<QuotationDto>.SuccessResponse(result, "Quotation approved"));
    }

    /// <summary>Reject a quotation (Coordinator)</summary>
    [HttpPost("coordinator/quotations/{id}/reject")]
    [Authorize(Roles = "SalesCoordinator")]
    public async Task<IActionResult> RejectQuotation(Guid id, [FromBody] RejectQuotationRequest request, CancellationToken ct)
    {
        await _quotationService.RejectQuotationAsync(GetUserId(), id, request, ct);
        return Ok(ApiResponse<string>.SuccessResponse("Quotation rejected"));
    }

    // ===== ADMIN ENDPOINTS =====

    /// <summary>Get all quotations (Admin)</summary>
    [HttpGet("admin/quotations")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public async Task<IActionResult> AdminGetQuotations([FromQuery] int page = 1, [FromQuery] int pageSize = 20,
        [FromQuery] string? status = null, [FromQuery] string? search = null, CancellationToken ct = default)
    {
        var result = await _quotationService.AdminGetAllQuotationsAsync(page, pageSize, status, search, ct);
        return Ok(ApiResponse<PagedResult<QuotationDto>>.SuccessResponse(result));
    }

    /// <summary>Get quotation by ID (Admin)</summary>
    [HttpGet("admin/quotations/{id}")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public async Task<IActionResult> AdminGetQuotation(Guid id, CancellationToken ct)
    {
        var result = await _quotationService.AdminGetQuotationByIdAsync(id, ct);
        return Ok(ApiResponse<QuotationDto>.SuccessResponse(result));
    }

    /// <summary>Approve a quotation (Admin)</summary>
    [HttpPost("admin/quotations/{id}/approve")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public async Task<IActionResult> AdminApproveQuotation(Guid id, [FromBody] ApproveQuotationRequest request, CancellationToken ct)
    {
        var result = await _quotationService.AdminApproveQuotationAsync(GetUserId(), id, request, ct);
        return Ok(ApiResponse<QuotationDto>.SuccessResponse(result, "Quotation approved"));
    }

    /// <summary>Reject a quotation (Admin)</summary>
    [HttpPost("admin/quotations/{id}/reject")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public async Task<IActionResult> AdminRejectQuotation(Guid id, [FromBody] RejectQuotationRequest request, CancellationToken ct)
    {
        await _quotationService.AdminRejectQuotationAsync(GetUserId(), id, request, ct);
        return Ok(ApiResponse<string>.SuccessResponse("Quotation rejected"));
    }

    /// <summary>Soft-delete (trash) a quotation (Admin)</summary>
    [HttpDelete("admin/quotations/{id}")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public async Task<IActionResult> AdminDeleteQuotation(Guid id, CancellationToken ct)
    {
        await _quotationService.AdminSoftDeleteAsync(id, GetUserId().ToString(), ct);
        return Ok(ApiResponse<string>.SuccessResponse("Quotation moved to trash"));
    }

    /// <summary>Get trash (Admin)</summary>
    [HttpGet("admin/quotations/trash")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public async Task<IActionResult> AdminGetTrash([FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
    {
        var result = await _quotationService.AdminGetTrashAsync(page, pageSize, ct);
        return Ok(ApiResponse<PagedResult<QuotationDto>>.SuccessResponse(result));
    }

    /// <summary>Restore a trashed quotation (Admin)</summary>
    [HttpPost("admin/quotations/{id}/restore")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public async Task<IActionResult> AdminRestoreQuotation(Guid id, CancellationToken ct)
    {
        await _quotationService.AdminRestoreAsync(id, ct);
        return Ok(ApiResponse<string>.SuccessResponse("Quotation restored"));
    }

    // ===== REP TRASH =====

    [HttpDelete("rep/quotations/{id}")]
    [Authorize(Roles = "SalesRep")]
    public async Task<IActionResult> RepDeleteQuotation(Guid id, CancellationToken ct)
    {
        await _quotationService.RepSoftDeleteAsync(id, GetUserId(), ct);
        return Ok(ApiResponse<string>.SuccessResponse("Quotation moved to trash"));
    }

    [HttpGet("rep/quotations/trash")]
    [Authorize(Roles = "SalesRep")]
    public async Task<IActionResult> RepGetQuotationTrash([FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
    {
        var result = await _quotationService.RepGetTrashAsync(GetUserId(), page, pageSize, ct);
        return Ok(ApiResponse<PagedResult<QuotationDto>>.SuccessResponse(result));
    }

    [HttpPost("rep/quotations/{id}/restore")]
    [Authorize(Roles = "SalesRep")]
    public async Task<IActionResult> RepRestoreQuotation(Guid id, CancellationToken ct)
    {
        await _quotationService.RepRestoreAsync(id, GetUserId(), ct);
        return Ok(ApiResponse<string>.SuccessResponse("Quotation restored"));
    }

    // ===== COORDINATOR TRASH =====

    [HttpDelete("coordinator/quotations/{id}")]
    [Authorize(Roles = "SalesCoordinator")]
    public async Task<IActionResult> CoordinatorDeleteQuotation(Guid id, CancellationToken ct)
    {
        await _quotationService.CoordinatorSoftDeleteAsync(id, GetUserId(), ct);
        return Ok(ApiResponse<string>.SuccessResponse("Quotation moved to trash"));
    }

    [HttpGet("coordinator/quotations/trash")]
    [Authorize(Roles = "SalesCoordinator")]
    public async Task<IActionResult> CoordinatorGetQuotationTrash([FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
    {
        var result = await _quotationService.CoordinatorGetTrashAsync(GetUserId(), page, pageSize, ct);
        return Ok(ApiResponse<PagedResult<QuotationDto>>.SuccessResponse(result));
    }

    [HttpPost("coordinator/quotations/{id}/restore")]
    [Authorize(Roles = "SalesCoordinator")]
    public async Task<IActionResult> CoordinatorRestoreQuotation(Guid id, CancellationToken ct)
    {
        await _quotationService.CoordinatorRestoreAsync(id, GetUserId(), ct);
        return Ok(ApiResponse<string>.SuccessResponse("Quotation restored"));
    }

    // ===== CUSTOMER TRASH =====

    [HttpDelete("customer/quotations/{id}")]
    [Authorize(Roles = "Customer")]
    public async Task<IActionResult> CustomerDeleteQuotation(Guid id, CancellationToken ct)
    {
        await _quotationService.CustomerSoftDeleteAsync(id, GetUserId(), ct);
        return Ok(ApiResponse<string>.SuccessResponse("Quotation moved to trash"));
    }

    [HttpGet("customer/quotations/trash")]
    [Authorize(Roles = "Customer")]
    public async Task<IActionResult> CustomerGetQuotationTrash([FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
    {
        var result = await _quotationService.CustomerGetTrashAsync(GetUserId(), page, pageSize, ct);
        return Ok(ApiResponse<PagedResult<QuotationDto>>.SuccessResponse(result));
    }

    [HttpPost("customer/quotations/{id}/restore")]
    [Authorize(Roles = "Customer")]
    public async Task<IActionResult> CustomerRestoreQuotation(Guid id, CancellationToken ct)
    {
        await _quotationService.CustomerRestoreAsync(id, GetUserId(), ct);
        return Ok(ApiResponse<string>.SuccessResponse("Quotation restored"));
    }
}

