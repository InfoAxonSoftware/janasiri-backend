using System.Security.Claims;
using DistributionSystem.Application.DTOs.Common;
using DistributionSystem.Application.DTOs.SalesSummary;
using DistributionSystem.Application.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DistributionSystem.API.Controllers;

[ApiController]
[Route("api")]
[Authorize]
[Produces("application/json")]
public class SalesSummaryReportController : ControllerBase
{
    private readonly ISalesSummaryReportService _service;
    public SalesSummaryReportController(ISalesSummaryReportService service) => _service = service;

    private Guid GetUserId() => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private string GetUsername() => User.FindFirstValue(ClaimTypes.Name) ?? User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    // ── Admin: upload (client-parsed Excel rows) ────────────────────────────

    /// <summary>Upload a client-parsed Sales Summary report for a region. Replaces any existing report for the same region + period.</summary>
    [HttpPost("admin/sales-summary/upload")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public async Task<IActionResult> Upload([FromBody] UploadSalesSummaryRequest request, CancellationToken ct)
    {
        if (request.Entries.Count == 0)
            return BadRequest(ApiResponse<string>.ErrorResponse("No data rows found in the file."));

        var result = await _service.UploadReportAsync(request, GetUsername(), ct);
        return Ok(ApiResponse<SalesSummaryReportDto>.SuccessResponse(result, "Report uploaded and processed."));
    }

    // ── Admin: list all reports ──────────────────────────────────────────────

    [HttpGet("admin/sales-summary")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public async Task<IActionResult> GetAll(CancellationToken ct)
    {
        var result = await _service.GetAllReportsAsync(ct);
        return Ok(ApiResponse<List<SalesSummaryReportDto>>.SuccessResponse(result));
    }

    // ── Admin: detail ─────────────────────────────────────────────────────────

    [HttpGet("admin/sales-summary/{reportId:guid}")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public async Task<IActionResult> GetById(Guid reportId, CancellationToken ct)
    {
        var result = await _service.GetReportByIdAsync(reportId, ct);
        return Ok(ApiResponse<SalesSummaryReportDetailDto>.SuccessResponse(result));
    }

    // ── Admin: delete ─────────────────────────────────────────────────────────

    [HttpDelete("admin/sales-summary/{reportId:guid}")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public async Task<IActionResult> Delete(Guid reportId, CancellationToken ct)
    {
        await _service.DeleteReportAsync(reportId, ct);
        return Ok(ApiResponse<string>.SuccessResponse("Report deleted."));
    }

    // ── Rep: own regions ──────────────────────────────────────────────────────

    /// <summary>Get Sales Summary reports for all regions assigned to the current sales rep.</summary>
    [HttpGet("rep/sales-summary")]
    [Authorize(Roles = "SalesRep")]
    public async Task<IActionResult> GetRepReports(CancellationToken ct)
    {
        var result = await _service.GetRepReportsAsync(GetUserId(), ct);
        return Ok(ApiResponse<List<SalesSummaryReportDetailDto>>.SuccessResponse(result));
    }

    [HttpGet("rep/sales-summary/{reportId:guid}")]
    [Authorize(Roles = "SalesRep")]
    public async Task<IActionResult> GetRepReportById(Guid reportId, CancellationToken ct)
    {
        var result = await _service.GetRepReportByIdAsync(reportId, GetUserId(), ct);
        return Ok(ApiResponse<SalesSummaryReportDetailDto>.SuccessResponse(result));
    }
}
