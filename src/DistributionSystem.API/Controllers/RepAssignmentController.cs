using System.Security.Claims;
using DistributionSystem.Application.DTOs.Common;
using DistributionSystem.Application.DTOs.QuickRequests;
using DistributionSystem.Application.Services.Implementations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace DistributionSystem.API.Controllers;
[ApiController]
[Authorize(Roles = "Admin,SuperAdmin")]
[Route("api/admin/rep-assignments/{kind}/{id:guid}")]
public sealed class RepAssignmentController(StandardRepAssignmentService service) : ControllerBase
{
    private Guid ActorId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    [HttpGet]
    public async Task<IActionResult> Get(string kind, Guid id, CancellationToken ct)
        => Ok(ApiResponse<StandardRepAssignmentDto>.SuccessResponse(await service.GetAsync(kind, id, ActorId, ct)));
    [HttpPut]
    public async Task<IActionResult> Put(string kind, Guid id, [FromBody] AssignGuestOrderRepsDto dto, CancellationToken ct)
        => Ok(ApiResponse<StandardRepAssignmentDto>.SuccessResponse(await service.SetAsync(kind, id, ActorId, dto, User.Identity?.Name ?? ActorId.ToString(), ct)));
}