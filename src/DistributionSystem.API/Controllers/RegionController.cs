using DistributionSystem.Application.DTOs.Common;
using DistributionSystem.Application.DTOs.Region;
using DistributionSystem.Application.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DistributionSystem.API.Controllers;

[ApiController]
[Route("api")]
[Produces("application/json")]
public class RegionController : ControllerBase
{
    private readonly IRegionService _regionService;

    public RegionController(IRegionService regionService)
    {
        _regionService = regionService;
    }

    [HttpGet("regions")]
    [Authorize]
    public async Task<IActionResult> GetAll(CancellationToken ct)
    {
        var result = await _regionService.GetAllRegionsAsync(ct);
        return Ok(ApiResponse<List<RegionDto>>.SuccessResponse(result));
    }

    [HttpGet("regions/{id:guid}")]
    [Authorize]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var result = await _regionService.GetRegionByIdAsync(id, ct);
        return Ok(ApiResponse<RegionDto>.SuccessResponse(result));
    }

    [HttpPost("admin/regions")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public async Task<IActionResult> Create([FromBody] CreateRegionRequest request, CancellationToken ct)
    {
        var result = await _regionService.CreateRegionAsync(request, ct);
        return CreatedAtAction(nameof(GetById), new { id = result.Id }, ApiResponse<RegionDto>.SuccessResponse(result, "Region created"));
    }

    [HttpPut("admin/regions/{id:guid}")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateRegionRequest request, CancellationToken ct)
    {
        var result = await _regionService.UpdateRegionAsync(id, request, ct);
        return Ok(ApiResponse<RegionDto>.SuccessResponse(result, "Region updated"));
    }

    [HttpDelete("admin/regions/{id:guid}")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await _regionService.DeleteRegionAsync(id, ct);
        return Ok(ApiResponse<object>.SuccessResponse(null!, "Region deleted"));
    }

    [HttpGet("regions/{regionId:guid}/sub-regions")]
    [Authorize]
    public async Task<IActionResult> GetSubRegions(Guid regionId, CancellationToken ct)
    {
        var result = await _regionService.GetSubRegionsByRegionAsync(regionId, ct);
        return Ok(ApiResponse<List<SubRegionDto>>.SuccessResponse(result));
    }

    [HttpPost("admin/sub-regions")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public async Task<IActionResult> CreateSubRegion([FromBody] CreateSubRegionRequest request, CancellationToken ct)
    {
        var result = await _regionService.CreateSubRegionAsync(request, ct);
        return Ok(ApiResponse<SubRegionDto>.SuccessResponse(result, "Sub-region created"));
    }

    [HttpPut("admin/sub-regions/{id:guid}")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public async Task<IActionResult> UpdateSubRegion(Guid id, [FromBody] UpdateSubRegionRequest request, CancellationToken ct)
    {
        var result = await _regionService.UpdateSubRegionAsync(id, request, ct);
        return Ok(ApiResponse<SubRegionDto>.SuccessResponse(result, "Sub-region updated"));
    }

    [HttpDelete("admin/sub-regions/{id:guid}")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public async Task<IActionResult> DeleteSubRegion(Guid id, CancellationToken ct)
    {
        await _regionService.DeleteSubRegionAsync(id, ct);
        return Ok(ApiResponse<object>.SuccessResponse(null!, "Sub-region deleted"));
    }
}

