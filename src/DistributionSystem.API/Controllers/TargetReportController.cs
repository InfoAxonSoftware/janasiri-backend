using System.Security.Claims;
using DistributionSystem.Application.DTOs.Common;
using DistributionSystem.Application.DTOs.TargetReports;
using DistributionSystem.Application.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DistributionSystem.API.Controllers;

[ApiController]
[Route("api")]
[Authorize]
[Produces("application/json")]
public class TargetReportController : ControllerBase
{
    private readonly ITargetReportService _service;
    public TargetReportController(ITargetReportService service) => _service = service;

    private Guid GetUserId() => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private string GetUsername() => User.FindFirstValue(ClaimTypes.Name) ?? User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    // ── Admin: upload / replace the current report for a target ────────────

    [HttpPost("admin/targets/{targetId:guid}/reports/upload")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public async Task<IActionResult> Upload(Guid targetId, [FromBody] UploadTargetReportRequest request, CancellationToken ct)
    {
        var result = await _service.UploadReportAsync(targetId, request, GetUserId(), GetUsername(), ct);
        return Ok(ApiResponse<TargetReportSummaryDto>.SuccessResponse(result, "Sales report uploaded."));
    }

    // ── Admin: upload history for a target ──────────────────────────────────

    [HttpGet("admin/targets/{targetId:guid}/reports")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public async Task<IActionResult> GetHistory(Guid targetId, CancellationToken ct)
    {
        var result = await _service.GetHistoryAsync(targetId, ct);
        return Ok(ApiResponse<List<TargetReportSummaryDto>>.SuccessResponse(result));
    }

    // ── Admin: current report detail for a target ───────────────────────────

    [HttpGet("admin/targets/{targetId:guid}/reports/current")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public async Task<IActionResult> GetCurrent(Guid targetId, CancellationToken ct)
    {
        var result = await _service.GetCurrentReportAsync(targetId, ct);
        return Ok(ApiResponse<TargetReportDetailDto>.SuccessResponse(result));
    }

    // ── Admin: any specific historical report detail (Upload History → View) ─

    [HttpGet("admin/target-reports/{reportId:guid}")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public async Task<IActionResult> GetById(Guid reportId, CancellationToken ct)
    {
        var result = await _service.GetReportByIdAsync(reportId, ct);
        return Ok(ApiResponse<TargetReportDetailDto>.SuccessResponse(result));
    }

    // ── Rep: current report for one of their own targets ────────────────────

    [HttpGet("rep/targets/{targetId:guid}/report")]
    [Authorize(Roles = "SalesRep")]
    public async Task<IActionResult> GetMyCurrentReport(Guid targetId, CancellationToken ct)
    {
        var result = await _service.GetRepCurrentReportAsync(targetId, GetUserId(), ct);
        return Ok(ApiResponse<TargetReportDetailDto>.SuccessResponse(result));
    }

    [HttpGet("rep/targets/{targetId:guid}/reports")]
    [Authorize(Roles = "SalesRep")]
    public async Task<IActionResult> GetMyReportHistory(Guid targetId, CancellationToken ct)
    {
        var result = await _service.GetRepHistoryAsync(targetId, GetUserId(), ct);
        return Ok(ApiResponse<List<TargetReportSummaryDto>>.SuccessResponse(result));
    }

    [HttpGet("rep/target-reports/{reportId:guid}")]
    [Authorize(Roles = "SalesRep")]
    public async Task<IActionResult> GetMyReportById(Guid reportId, CancellationToken ct)
    {
        var result = await _service.GetRepReportByIdAsync(reportId, GetUserId(), ct);
        return Ok(ApiResponse<TargetReportDetailDto>.SuccessResponse(result));
    }
}
