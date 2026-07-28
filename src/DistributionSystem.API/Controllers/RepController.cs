using System.Security.Claims;
using DistributionSystem.Application.DTOs.Common;
using DistributionSystem.Application.DTOs.Rep;
using DistributionSystem.Application.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DistributionSystem.API.Controllers;

/// <summary>
/// Sales representative management endpoints
/// </summary>
[ApiController]
[Route("api")]
[Authorize]
[Produces("application/json")]
public class RepController : ControllerBase
{
    private readonly IRepService _repService;

    public RepController(IRepService repService)
    {
        _repService = repService;
    }

    private Guid GetUserId() => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    // ===== ADMIN ENDPOINTS =====

    /// <summary>Get all sales reps (Admin)</summary>
    [HttpGet("admin/reps")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public async Task<IActionResult> GetAllReps([FromQuery] int page = 1, [FromQuery] int pageSize = 20,
        [FromQuery] string? search = null, [FromQuery] Guid? coordinatorId = null,
        [FromQuery] Guid? regionId = null, [FromQuery] bool? isActive = null, CancellationToken ct = default)
    {
        var result = await _repService.GetAllAsync(page, pageSize, search, coordinatorId, regionId, isActive, ct);
        return Ok(ApiResponse<PagedResult<RepDto>>.SuccessResponse(result));
    }

    /// <summary>Get rep by ID (Admin)</summary>
    [HttpGet("admin/reps/{id}")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public async Task<IActionResult> GetRep(Guid id, CancellationToken ct)
    {
        var result = await _repService.GetByIdAsync(id, ct);
        return Ok(ApiResponse<RepDto>.SuccessResponse(result));
    }

    /// <summary>Create a new sales rep (Admin)</summary>
    [HttpPost("admin/reps")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public async Task<IActionResult> CreateRep([FromBody] CreateRepRequest request, CancellationToken ct)
    {
        var result = await _repService.CreateAsync(request, ct);
        return CreatedAtAction(nameof(GetRep), new { id = result.Id }, ApiResponse<RepDto>.SuccessResponse(result, "Rep created"));
    }

    /// <summary>Update a sales rep (Admin)</summary>
    [HttpPut("admin/reps/{id}")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public async Task<IActionResult> UpdateRep(Guid id, [FromBody] UpdateRepRequest request, CancellationToken ct)
    {
        var result = await _repService.UpdateAsync(id, request, ct);
        return Ok(ApiResponse<RepDto>.SuccessResponse(result, "Rep updated"));
    }

    /// <summary>Unassign a coordinator from a rep (Admin)</summary>
    [HttpDelete("admin/reps/{id}/coordinator")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public async Task<IActionResult> UnassignCoordinator(Guid id, CancellationToken ct)
    {
        await _repService.UnassignCoordinatorAsync(id, ct);
        return Ok(ApiResponse<string>.SuccessResponse("Coordinator unassigned"));
    }

    /// <summary>Soft-delete a rep — moves to trash (Admin)</summary>
    [HttpDelete("admin/reps/{id}")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public async Task<IActionResult> SoftDeleteRep(Guid id, CancellationToken ct)
    {
        var deletedBy = User.FindFirstValue(System.Security.Claims.ClaimTypes.Name) ?? GetUserId().ToString();
        await _repService.SoftDeleteAsync(id, deletedBy, ct);
        return Ok(ApiResponse<string>.SuccessResponse("Rep moved to trash"));
    }

    /// <summary>Get trashed reps (Admin)</summary>
    [HttpGet("admin/reps/trash")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public async Task<IActionResult> GetTrashedReps([FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
    {
        var result = await _repService.GetTrashedAsync(page, pageSize, ct);
        return Ok(ApiResponse<PagedResult<RepDto>>.SuccessResponse(result));
    }

    /// <summary>Restore a rep from trash (Admin)</summary>
    [HttpPost("admin/reps/{id}/restore")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public async Task<IActionResult> RestoreRep(Guid id, CancellationToken ct)
    {
        await _repService.RestoreAsync(id, ct);
        return Ok(ApiResponse<string>.SuccessResponse("Rep restored"));
    }

    /// <summary>Get rep performance metrics (Admin)</summary>
    [HttpGet("admin/reps/{id}/performance")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public async Task<IActionResult> GetRepPerformance(Guid id, [FromQuery] DateTime? from, [FromQuery] DateTime? to, CancellationToken ct)
    {
        var result = await _repService.GetPerformanceAsync(id, from ?? DateTime.UtcNow.AddMonths(-1), to ?? DateTime.UtcNow, ct);
        return Ok(ApiResponse<RepPerformanceDto>.SuccessResponse(result));
    }

    /// <summary>Set sales targets for rep (Admin)</summary>
    [HttpPost("admin/reps/{id}/targets")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public async Task<IActionResult> SetTarget(Guid id, [FromBody] SalesTargetDto target, CancellationToken ct)
    {
        await _repService.SetTargetAsync(id, target, ct);
        return Ok(ApiResponse<string>.SuccessResponse("Target set"));
    }

    /// <summary>Get targets for a rep (Admin)</summary>
    [HttpGet("admin/reps/{id}/targets")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public async Task<IActionResult> GetRepTargets(Guid id, CancellationToken ct)
    {
        var result = await _repService.GetTargetsByRepIdAsync(id, ct);
        return Ok(ApiResponse<List<SalesTargetDto>>.SuccessResponse(result));
    }

    /// <summary>Delete a sales target (Admin)</summary>
    [HttpDelete("admin/targets/{targetId}")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public async Task<IActionResult> DeleteTarget(Guid targetId, CancellationToken ct)
    {
        await _repService.DeleteTargetAsync(targetId, ct);
        return Ok(ApiResponse<string>.SuccessResponse("Target deleted"));
    }

    // ===== ADMIN ROUTE MANAGEMENT =====

    /// <summary>Get all routes (Admin)</summary>
    [HttpGet("admin/routes")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public async Task<IActionResult> GetAllRoutes(CancellationToken ct)
    {
        var result = await _repService.GetAllRoutesAsync(ct);
        return Ok(ApiResponse<List<RouteDto>>.SuccessResponse(result));
    }

    /// <summary>Create a route (Admin)</summary>
    [HttpPost("admin/routes")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public async Task<IActionResult> CreateRoute([FromBody] CreateRouteRequest request, CancellationToken ct)
    {
        var result = await _repService.CreateRouteAsync(request, ct);
        return Ok(ApiResponse<RouteDto>.SuccessResponse(result, "Route created"));
    }

    /// <summary>Add customer to route (Admin)</summary>
    [HttpPost("admin/routes/{routeId}/customers")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public async Task<IActionResult> AddCustomerToRoute(Guid routeId, [FromBody] AddRouteCustomerRequest request, CancellationToken ct)
    {
        await _repService.AddCustomerToRouteAsync(routeId, request, ct);
        return Ok(ApiResponse<string>.SuccessResponse("Customer added to route"));
    }

    /// <summary>Assign route to rep (Admin)</summary>
    [HttpPost("admin/routes/{routeId}/assign")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public async Task<IActionResult> AssignRoute(Guid routeId, [FromBody] AssignRouteRequest request, CancellationToken ct)
    {
        await _repService.AssignRouteAsync(routeId, request, ct);
        return Ok(ApiResponse<string>.SuccessResponse("Route assigned to rep"));
    }

    /// <summary>Delete (soft) a route (Admin)</summary>
    [HttpDelete("admin/routes/{routeId}")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public async Task<IActionResult> DeleteRoute(Guid routeId, CancellationToken ct)
    {
        await _repService.DeleteRouteAsync(routeId, ct);
        return Ok(ApiResponse<string>.SuccessResponse("Route deleted"));
    }

    /// <summary>Remove customer from route (Admin)</summary>
    [HttpDelete("admin/routes/{routeId}/customers/{customerId}")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public async Task<IActionResult> RemoveCustomerFromRoute(Guid routeId, Guid customerId, CancellationToken ct)
    {
        await _repService.RemoveCustomerFromRouteAsync(routeId, customerId, ct);
        return Ok(ApiResponse<string>.SuccessResponse("Customer removed from route"));
    }

    /// <summary>Remove rep from route (Admin)</summary>
    [HttpDelete("admin/routes/{routeId}/reps/{repId}")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public async Task<IActionResult> RemoveRepFromRoute(Guid routeId, Guid repId, CancellationToken ct)
    {
        await _repService.RemoveRepFromRouteAsync(routeId, repId, ct);
        return Ok(ApiResponse<string>.SuccessResponse("Rep removed from route"));
    }

    /// <summary>Return today's strike rate progress per rep (Admin)</summary>
    [HttpGet("admin/routes/progress/today")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public async Task<IActionResult> GetRouteProgress(CancellationToken ct)
    {
        var result = await _repService.GetTodayRouteProgressAsync(ct);
        return Ok(ApiResponse<List<RouteProgressDto>>.SuccessResponse(result));
    }

    // ===== REP ENDPOINTS =====

    /// <summary>Get my profile (Rep)</summary>
    [HttpGet("rep/profile")]
    [Authorize(Roles = "SalesRep")]
    public async Task<IActionResult> GetMyProfile(CancellationToken ct)
    {
        var userId = GetUserId();
        var result = await _repService.GetByUserIdAsync(userId, ct);
        return Ok(ApiResponse<RepDto>.SuccessResponse(result));
    }

    /// <summary>Update my profile (Rep)</summary>
    [HttpPut("rep/profile")]
    [Authorize(Roles = "SalesRep")]
    public async Task<IActionResult> UpdateMyProfile([FromBody] UpdateRepRequest request, CancellationToken ct)
    {
        var userId = GetUserId();
        var result = await _repService.UpdateByUserIdAsync(userId, request, ct);
        return Ok(ApiResponse<RepDto>.SuccessResponse(result, "Profile updated"));
    }

    /// <summary>Add an ad-hoc visit for today (Rep)</summary>
    [HttpPost("rep/visits/ad-hoc")]
    [Authorize(Roles = "SalesRep")]
    public async Task<IActionResult> AddAdHocVisit([FromBody] DistributionSystem.Application.DTOs.Rep.AdHocVisitRequest request, CancellationToken ct)
    {
        var userId = GetUserId();
        var result = await _repService.AddAdHocVisitAsync(userId, request.CustomerId, request.Notes, ct);
        return Ok(ApiResponse<VisitDto>.SuccessResponse(result, "Visit added"));
    }

    /// <summary>Trigger nightly visit generator (Admin only)</summary>
    [HttpPost("admin/run-visit-generator")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public async Task<IActionResult> RunVisitGenerator(CancellationToken ct)
    {
        await _repService.GenerateTodayVisitsAsync(ct);
        return Ok(ApiResponse<string>.SuccessResponse("Executed"));
    }

    /// <summary>Get my routes (Rep)</summary>
    [HttpGet("rep/routes")]
    [Authorize(Roles = "SalesRep")]
    public async Task<IActionResult> GetMyRoutes(CancellationToken ct)
    {
        var userId = GetUserId();
        var result = await _repService.GetRepRoutesAsync(userId, ct);
        return Ok(ApiResponse<List<RouteDto>>.SuccessResponse(result));
    }

    /// <summary>Get single visit details (Rep)</summary>
    [HttpGet("rep/visits/{visitId}")]
    [Authorize(Roles = "SalesRep")]
    public async Task<IActionResult> GetVisit(Guid visitId, CancellationToken ct)
    {
        var userId = GetUserId();
        var result = await _repService.GetVisitByIdAsync(userId, visitId, ct);
        return Ok(ApiResponse<VisitDto>.SuccessResponse(result));
    }

    /// <summary>Get route details (Rep)</summary>
    [HttpGet("rep/routes/{routeId}")]
    [Authorize(Roles = "SalesRep")]
    public async Task<IActionResult> GetRoute(Guid routeId, CancellationToken ct)
    {
        var userId = GetUserId();
        var result = await _repService.GetRouteDetailsAsync(userId, routeId, ct);
        return Ok(ApiResponse<RouteDto>.SuccessResponse(result));
    }

    /// <summary>Get today's visits (Rep)</summary>
    [HttpGet("rep/visits/today")]
    [Authorize(Roles = "SalesRep")]
    public async Task<IActionResult> GetTodayVisits(CancellationToken ct)
    {
        var userId = GetUserId();
        var result = await _repService.GetTodayVisitsAsync(userId, ct);
        return Ok(ApiResponse<List<VisitDto>>.SuccessResponse(result));
    }

    /// <summary>Check in at customer location (Rep)</summary>
    [HttpPost("rep/visits/check-in")]
    [Authorize(Roles = "SalesRep")]
    public async Task<IActionResult> CheckIn([FromBody] CheckInRequest request, CancellationToken ct)
    {
        var userId = GetUserId();
        var result = await _repService.CheckInAsync(userId, request, ct);
        return Ok(ApiResponse<VisitDto>.SuccessResponse(result, "Checked in"));
    }

    /// <summary>Cancel a planned visit (Rep)</summary>
    [HttpDelete("rep/visits/{visitId}")]
    [Authorize(Roles = "SalesRep")]
    public async Task<IActionResult> CancelVisit(Guid visitId, CancellationToken ct)
    {
        var userId = GetUserId();
        await _repService.CancelVisitAsync(userId, visitId, ct);
        return Ok(ApiResponse<string>.SuccessResponse("Cancelled"));
    }

    /// <summary>Check out from customer location (Rep)</summary>
    [HttpPost("rep/visits/{visitId}/check-out")]
    [Authorize(Roles = "SalesRep")]
    public async Task<IActionResult> CheckOut(Guid visitId, [FromBody] CheckOutRequest request, CancellationToken ct)
    {
        var result = await _repService.CheckOutAsync(visitId, request, ct);
        return Ok(ApiResponse<VisitDto>.SuccessResponse(result, "Checked out"));
    }

    /// <summary>Get my performance dashboard (Rep)</summary>
    [HttpGet("rep/performance")]
    [Authorize(Roles = "SalesRep")]
    public async Task<IActionResult> GetMyPerformance([FromQuery] DateTime? from, [FromQuery] DateTime? to, CancellationToken ct)
    {
        var userId = GetUserId();
        var result = await _repService.GetPerformanceByUserIdAsync(userId, from ?? DateTime.UtcNow.AddMonths(-1), to ?? DateTime.UtcNow, ct);
        return Ok(ApiResponse<RepPerformanceDto>.SuccessResponse(result));
    }

    /// <summary>Get my sales targets (Rep)</summary>
    [HttpGet("rep/targets")]
    [Authorize(Roles = "SalesRep")]
    public async Task<IActionResult> GetMyTargets(CancellationToken ct)
    {
        var userId = GetUserId();
        var result = await _repService.GetTargetsAsync(userId, ct);
        return Ok(ApiResponse<List<SalesTargetDto>>.SuccessResponse(result));
    }

}

