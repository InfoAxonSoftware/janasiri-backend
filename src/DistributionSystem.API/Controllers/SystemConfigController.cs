using DistributionSystem.Application.DTOs.Common;
using DistributionSystem.Application.DTOs.Config;
using DistributionSystem.Application.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DistributionSystem.API.Controllers;

/// <summary>
/// System configuration endpoints
/// </summary>
[ApiController]
[Route("api")]
[Produces("application/json")]
public class SystemConfigController : ControllerBase
{
    private readonly ISystemConfigService _configService;

    public SystemConfigController(ISystemConfigService configService)
    {
        _configService = configService;
    }

    /// <summary>Get system configuration (public — used for branding)</summary>
    [HttpGet("config")]
    public async Task<IActionResult> GetConfig(CancellationToken ct)
    {
        var result = await _configService.GetConfigAsync(ct);
        return Ok(ApiResponse<SystemConfigDto>.SuccessResponse(result));
    }

    /// <summary>Update system configuration (Admin)</summary>
    [HttpPut("admin/config")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public async Task<IActionResult> UpdateConfig([FromBody] UpdateSystemConfigRequest request, CancellationToken ct)
    {
        var result = await _configService.UpdateConfigAsync(request, ct);
        return Ok(ApiResponse<SystemConfigDto>.SuccessResponse(result, "Configuration updated"));
    }
}

