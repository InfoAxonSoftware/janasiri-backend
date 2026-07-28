using System.Security.Claims;
using DistributionSystem.Application.DTOs.Common;
using DistributionSystem.Application.DTOs.Coordinator;
using DistributionSystem.Application.DTOs.Customer;
using DistributionSystem.Application.DTOs.Rep;
using DistributionSystem.Application.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DistributionSystem.API.Controllers;

/// <summary>
/// Sales Coordinator management and operations
/// </summary>
[ApiController]
[Route("api")]
[Authorize]
[Produces("application/json")]
public class CoordinatorController : ControllerBase
{
    private readonly ICoordinatorService _coordinatorService;

    public CoordinatorController(ICoordinatorService coordinatorService)
    {
        _coordinatorService = coordinatorService;
    }

    private Guid GetUserId() => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    // ===== ADMIN ENDPOINTS =====

    /// <summary>Get all coordinators (Admin)</summary>
    [HttpGet("admin/coordinators")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public async Task<IActionResult> GetAllCoordinators([FromQuery] int page = 1, [FromQuery] int pageSize = 20,
        [FromQuery] string? search = null, CancellationToken ct = default)
    {
        var result = await _coordinatorService.GetAllCoordinatorsAsync(page, pageSize, search, ct);
        return Ok(ApiResponse<PagedResult<CoordinatorDto>>.SuccessResponse(result));
    }

    /// <summary>Get coordinator by ID (Admin)</summary>
    [HttpGet("admin/coordinators/{id}")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public async Task<IActionResult> GetCoordinator(Guid id, CancellationToken ct)
    {
        var result = await _coordinatorService.GetCoordinatorByIdAsync(id, ct);
        return Ok(ApiResponse<CoordinatorDto>.SuccessResponse(result));
    }

    /// <summary>Create a new coordinator (Admin)</summary>
    [HttpPost("admin/coordinators")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public async Task<IActionResult> CreateCoordinator([FromBody] CreateCoordinatorRequest request, CancellationToken ct)
    {
        var result = await _coordinatorService.CreateCoordinatorAsync(request, ct);
        return CreatedAtAction(nameof(GetCoordinator), new { id = result.Id }, ApiResponse<CoordinatorDto>.SuccessResponse(result, "Coordinator created"));
    }

    /// <summary>Update a coordinator (Admin)</summary>
    [HttpPut("admin/coordinators/{id}")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public async Task<IActionResult> UpdateCoordinator(Guid id, [FromBody] UpdateCoordinatorRequest request, CancellationToken ct)
    {
        var result = await _coordinatorService.UpdateCoordinatorAsync(id, request, ct);
        return Ok(ApiResponse<CoordinatorDto>.SuccessResponse(result, "Coordinator updated"));
    }

    /// <summary>Delete a coordinator (soft-delete — moves to trash) (Admin)</summary>
    [HttpDelete("admin/coordinators/{id}")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public async Task<IActionResult> DeleteCoordinator(Guid id, CancellationToken ct)
    {
        var deletedBy = User.FindFirstValue(ClaimTypes.Name) ?? GetUserId().ToString();
        await _coordinatorService.SoftDeleteAsync(id, deletedBy, ct);
        return Ok(ApiResponse<string>.SuccessResponse("Coordinator moved to trash"));
    }

    /// <summary>Get trashed coordinators (Admin)</summary>
    [HttpGet("admin/coordinators/trash")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public async Task<IActionResult> GetTrashedCoordinators([FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
    {
        var result = await _coordinatorService.GetTrashedAsync(page, pageSize, ct);
        return Ok(ApiResponse<PagedResult<CoordinatorDto>>.SuccessResponse(result));
    }

    /// <summary>Restore a coordinator from trash (Admin)</summary>
    [HttpPost("admin/coordinators/{id}/restore")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public async Task<IActionResult> RestoreCoordinator(Guid id, CancellationToken ct)
    {
        await _coordinatorService.RestoreAsync(id, ct);
        return Ok(ApiResponse<string>.SuccessResponse("Coordinator restored"));
    }

    /// <summary>Assign a sales rep to a coordinator (Admin)</summary>
    [HttpPost("admin/coordinators/{coordinatorId}/assign-rep")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public async Task<IActionResult> AssignRep(Guid coordinatorId, [FromBody] AssignRepToCoordinatorRequest request, CancellationToken ct)
    {
        await _coordinatorService.AssignRepToCoordinatorAsync(coordinatorId, request.RepId, ct);
        return Ok(ApiResponse<string>.SuccessResponse("Rep assigned to coordinator"));
    }

    // ===== COORDINATOR ENDPOINTS =====

    /// <summary>Get coordinator profile</summary>
    [HttpGet("coordinator/profile")]
    [Authorize(Roles = "SalesCoordinator")]
    public async Task<IActionResult> GetMyProfile(CancellationToken ct)
    {
        var result = await _coordinatorService.GetMyProfileAsync(GetUserId(), ct);
        return Ok(ApiResponse<CoordinatorDto>.SuccessResponse(result));
    }

    /// <summary>Update coordinator profile</summary>
    [HttpPut("coordinator/profile")]
    [Authorize(Roles = "SalesCoordinator")]
    public async Task<IActionResult> UpdateMyProfile([FromBody] UpdateCoordinatorRequest request, CancellationToken ct)
    {
        var result = await _coordinatorService.UpdateMyProfileAsync(GetUserId(), request, ct);
        return Ok(ApiResponse<CoordinatorDto>.SuccessResponse(result, "Profile updated"));
    }

    /// <summary>Get coordinator dashboard</summary>
    [HttpGet("coordinator/dashboard")]
    [Authorize(Roles = "SalesCoordinator")]
    public async Task<IActionResult> GetDashboard(CancellationToken ct)
    {
        var result = await _coordinatorService.GetDashboardAsync(GetUserId(), ct);
        return Ok(ApiResponse<CoordinatorDashboardDto>.SuccessResponse(result));
    }

    /// <summary>Get assigned reps (Region and Team)</summary>
    [HttpGet("coordinator/reps")]
    [Authorize(Roles = "SalesCoordinator")]
    public async Task<IActionResult> GetAssignedReps(CancellationToken ct)
    {
        var result = await _coordinatorService.GetAssignedRepsAsync(GetUserId(), ct);
        return Ok(ApiResponse<List<RepDto>>.SuccessResponse(result));
    }

    /// <summary>Get one assigned rep details</summary>
    [HttpGet("coordinator/reps/{repId}")]
    [Authorize(Roles = "SalesCoordinator")]
    public async Task<IActionResult> GetAssignedRepById(Guid repId, CancellationToken ct)
    {
        var result = await _coordinatorService.GetAssignedRepByIdAsync(GetUserId(), repId, ct);
        return Ok(ApiResponse<RepDto>.SuccessResponse(result));
    }

    /// <summary>Get one assigned rep performance</summary>
    [HttpGet("coordinator/reps/{repId}/performance")]
    [Authorize(Roles = "SalesCoordinator")]
    public async Task<IActionResult> GetAssignedRepPerformance(Guid repId, [FromQuery] DateTime? from, [FromQuery] DateTime? to, CancellationToken ct)
    {
        var result = await _coordinatorService.GetRepPerformanceAsync(GetUserId(), repId, from ?? DateTime.UtcNow.AddMonths(-1), to ?? DateTime.UtcNow, ct);
        return Ok(ApiResponse<RepPerformanceDto>.SuccessResponse(result));
    }

    /// <summary>Get customers assigned to one rep</summary>
    [HttpGet("coordinator/reps/{repId}/customers")]
    [Authorize(Roles = "SalesCoordinator")]
    public async Task<IActionResult> GetAssignedRepCustomers(Guid repId, [FromQuery] int page = 1, [FromQuery] int pageSize = 50, [FromQuery] string? search = null, CancellationToken ct = default)
    {
        var result = await _coordinatorService.GetRepCustomersAsync(GetUserId(), repId, page, pageSize, search, ct);
        return Ok(ApiResponse<PagedResult<CustomerDto>>.SuccessResponse(result));
    }

    /// <summary>Get routes for one assigned rep</summary>
    [HttpGet("coordinator/reps/{repId}/routes")]
    [Authorize(Roles = "SalesCoordinator")]
    public async Task<IActionResult> GetAssignedRepRoutes(Guid repId, CancellationToken ct)
    {
        var result = await _coordinatorService.GetRepRoutesAsync(GetUserId(), repId, ct);
        return Ok(ApiResponse<List<RouteDto>>.SuccessResponse(result));
    }

    /// <summary>Get all routes owned by coordinator</summary>
    [HttpGet("coordinator/routes")]
    [Authorize(Roles = "SalesCoordinator")]
    public async Task<IActionResult> GetRoutes(CancellationToken ct)
    {
        var result = await _coordinatorService.GetRoutesAsync(GetUserId(), ct);
        return Ok(ApiResponse<List<RouteDto>>.SuccessResponse(result));
    }

    /// <summary>Create route under coordinator management (optionally assign rep)</summary>
    [HttpPost("coordinator/routes")]
    [Authorize(Roles = "SalesCoordinator")]
    public async Task<IActionResult> CreateRoute([FromBody] CreateRouteRequest request, CancellationToken ct)
    {
        var result = await _coordinatorService.CreateRouteAsync(GetUserId(), request, ct);
        return Ok(ApiResponse<RouteDto>.SuccessResponse(result, "Route created"));
    }

    /// <summary>Assign route to one rep</summary>
    [HttpPost("coordinator/routes/{routeId}/assign")]
    [Authorize(Roles = "SalesCoordinator")]
    public async Task<IActionResult> AssignRoute(Guid routeId, [FromBody] AssignRouteRequest request, CancellationToken ct)
    {
        await _coordinatorService.AssignRouteAsync(GetUserId(), routeId, request.RepId, ct);
        return Ok(ApiResponse<string>.SuccessResponse("Route assigned"));
    }

    /// <summary>Create route for one assigned rep</summary>
    [HttpPost("coordinator/reps/{repId}/routes")]
    [Authorize(Roles = "SalesCoordinator")]
    public async Task<IActionResult> CreateRouteForRep(Guid repId, [FromBody] CreateRouteRequest request, CancellationToken ct)
    {
        var result = await _coordinatorService.CreateRouteForRepAsync(GetUserId(), repId, request, ct);
        return Ok(ApiResponse<RouteDto>.SuccessResponse(result, "Route created"));
    }

    /// <summary>Add customer to route</summary>
    [HttpPost("coordinator/routes/{routeId}/customers")]
    [Authorize(Roles = "SalesCoordinator")]
    public async Task<IActionResult> AddCustomerToRoute(Guid routeId, [FromBody] AddRouteCustomerRequest request, CancellationToken ct)
    {
        await _coordinatorService.AddCustomerToRouteAsync(GetUserId(), routeId, request, ct);
        return Ok(ApiResponse<string>.SuccessResponse("Customer added to route"));
    }

    /// <summary>Remove customer from route</summary>
    [HttpDelete("coordinator/routes/{routeId}/customers/{customerId}")]
    [Authorize(Roles = "SalesCoordinator")]
    public async Task<IActionResult> RemoveCustomerFromRoute(Guid routeId, Guid customerId, CancellationToken ct)
    {
        await _coordinatorService.RemoveCustomerFromRouteAsync(GetUserId(), routeId, customerId, ct);
        return Ok(ApiResponse<string>.SuccessResponse("Customer removed from route"));
    }

    /// <summary>Delete route</summary>
    [HttpDelete("coordinator/routes/{routeId}")]
    [Authorize(Roles = "SalesCoordinator")]
    public async Task<IActionResult> DeleteRoute(Guid routeId, CancellationToken ct)
    {
        await _coordinatorService.DeleteRouteAsync(GetUserId(), routeId, ct);
        return Ok(ApiResponse<string>.SuccessResponse("Route deleted"));
    }

    /// <summary>Get region customers</summary>
    [HttpGet("coordinator/customers")]
    [Authorize(Roles = "SalesCoordinator")]
    public async Task<IActionResult> GetRegionCustomers([FromQuery] int page = 1, [FromQuery] int pageSize = 20,
        [FromQuery] string? search = null, CancellationToken ct = default)
    {
        var result = await _coordinatorService.GetRegionCustomersAsync(GetUserId(), page, pageSize, search, ct);
        return Ok(ApiResponse<PagedResult<CustomerDto>>.SuccessResponse(result));
    }

    /// <summary>Get pending customer approvals</summary>
    [HttpGet("coordinator/customers/pending")]
    [Authorize(Roles = "SalesCoordinator")]
    public async Task<IActionResult> GetPendingApprovals([FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
    {
        var result = await _coordinatorService.GetPendingCustomerApprovalsAsync(GetUserId(), page, pageSize, ct);
        return Ok(ApiResponse<PagedResult<CustomerDto>>.SuccessResponse(result));
    }

    /// <summary>Approve a customer registration</summary>
    [HttpPost("coordinator/customers/{customerId}/approve")]
    [Authorize(Roles = "SalesCoordinator")]
    public async Task<IActionResult> ApproveCustomer(Guid customerId, [FromBody] ApproveCustomerRequest request, CancellationToken ct)
    {
        var result = await _coordinatorService.ApproveCustomerAsync(GetUserId(), customerId, request, ct);
        return Ok(ApiResponse<CustomerDto>.SuccessResponse(result, "Customer approved"));
    }

    /// <summary>Reject a customer registration</summary>
    [HttpPost("coordinator/customers/{customerId}/reject")]
    [Authorize(Roles = "SalesCoordinator")]
    public async Task<IActionResult> RejectCustomer(Guid customerId, [FromBody] RejectCustomerRequest request, CancellationToken ct)
    {
        await _coordinatorService.RejectCustomerAsync(GetUserId(), customerId, request, ct);
        return Ok(ApiResponse<string>.SuccessResponse("Customer rejected"));
    }

}

