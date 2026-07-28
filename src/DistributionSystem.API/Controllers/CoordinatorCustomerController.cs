using System.Security.Claims;
using DistributionSystem.Application.DTOs.Common;
using DistributionSystem.Application.DTOs.Customer;
using DistributionSystem.Application.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DistributionSystem.API.Controllers;

[ApiController]
[Route("api/coordinator/customer-management")]
[Authorize(Roles = "SalesCoordinator")]
[Produces("application/json")]
public class CoordinatorCustomerController : ControllerBase
{
    private readonly ICustomerService _customerService;
    private readonly ICoordinatorService _coordinatorService;

    public CoordinatorCustomerController(ICustomerService customerService, ICoordinatorService coordinatorService)
    {
        _customerService = customerService;
        _coordinatorService = coordinatorService;
    }

    private Guid GetUserId() => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpGet]
    public async Task<IActionResult> GetCustomers(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? search = null,
        [FromQuery] string? sortBy = null,
        [FromQuery] string? sortOrder = "asc",
        [FromQuery] bool? isActive = null,
        [FromQuery] Guid? assignedRepId = null,
        [FromQuery] Guid? regionId = null,
        [FromQuery] Guid? subRegionId = null,
        [FromQuery] DateTime? createdFrom = null,
        [FromQuery] DateTime? createdTo = null,
        CancellationToken ct = default)
    {
        var coordinator = await _coordinatorService.GetMyProfileAsync(GetUserId(), ct);
        var result = await _customerService.GetAllAsync(
            page,
            pageSize,
            search,
            sortBy,
            sortOrder,
            isActive,
            assignedRepId,
            coordinator.Id,
            regionId,
            subRegionId,
            null,
            null,
            null,
            createdFrom,
            createdTo,
            ct);

        return Ok(ApiResponse<PagedResult<CustomerDto>>.SuccessResponse(result));
    }

    [HttpGet("filters")]
    public async Task<IActionResult> GetFilterOptions(CancellationToken ct = default)
    {
        var coordinator = await _coordinatorService.GetMyProfileAsync(GetUserId(), ct);
        var assignedReps = await _coordinatorService.GetAssignedRepsAsync(GetUserId(), ct);
        var allowedRepIds = assignedReps.Select(r => r.Id).ToHashSet();

        var options = await _customerService.GetFilterOptionsAsync(ct);
        options.Coordinators = options.Coordinators.Where(c => c.Id == coordinator.Id).ToList();
        options.AssignedReps = options.AssignedReps.Where(r => allowedRepIds.Contains(r.Id)).ToList();
        return Ok(ApiResponse<CustomerFilterOptionsDto>.SuccessResponse(options));
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetCustomer(Guid id, CancellationToken ct)
    {
        var coordinator = await _coordinatorService.GetMyProfileAsync(GetUserId(), ct);
        var customer = await _customerService.GetByIdAsync(id, ct);
        if (customer.AssignedCoordinatorId != coordinator.Id)
            return Forbid();

        return Ok(ApiResponse<CustomerDto>.SuccessResponse(customer));
    }

    [HttpGet("{id:guid}/summary")]
    public async Task<IActionResult> GetCustomerSummary(Guid id, CancellationToken ct)
    {
        var coordinator = await _coordinatorService.GetMyProfileAsync(GetUserId(), ct);
        var customer = await _customerService.GetByIdAsync(id, ct);
        if (customer.AssignedCoordinatorId != coordinator.Id)
            return Forbid();

        var baseUrl = "";
        var summary = await _customerService.GetSummaryAsync(id, baseUrl, ct);
        return Ok(ApiResponse<CustomerSummaryDto>.SuccessResponse(summary));
    }
}
