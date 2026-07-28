using System.Security.Claims;
using DistributionSystem.Application.DTOs.Common;
using DistributionSystem.Application.DTOs.Stock;
using DistributionSystem.Application.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DistributionSystem.API.Controllers;

[ApiController]
[Route("api")]
[Authorize]
[Produces("application/json")]
public class StockReportController : ControllerBase
{
    private readonly IStockReportService _service;
    public StockReportController(IStockReportService service) => _service = service;

    private Guid GetUserId() => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private string GetUsername() => User.FindFirstValue(ClaimTypes.Name) ?? User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    // ── Admin: upload ────────────────────────────────────────────────────────

    /// <summary>Upload a stock summary Excel file for a region (Admin)</summary>
    [HttpPost("admin/stock-reports/upload")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    [RequestSizeLimit(20 * 1024 * 1024)] // 20 MB
    public async Task<IActionResult> Upload(
        [FromForm] Guid regionId,
        IFormFile file,
        CancellationToken ct)
    {
        if (file == null || file.Length == 0)
            return BadRequest(ApiResponse<string>.ErrorResponse("No file uploaded."));

        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (ext is not ".xlsx" and not ".xls")
            return BadRequest(ApiResponse<string>.ErrorResponse("Only .xlsx and .xls files are supported."));

        var result = await _service.UploadReportAsync(regionId, file, GetUsername(), ct);
        return Ok(ApiResponse<StockReportSummaryDto>.SuccessResponse(result, "Report uploaded and processed."));
    }

    // ── Admin: list all regions' reports ────────────────────────────────────

    /// <summary>Get summary of all uploaded stock reports (Admin)</summary>
    [HttpGet("admin/stock-reports")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public async Task<IActionResult> GetAll(CancellationToken ct)
    {
        var result = await _service.GetAllReportsAsync(ct);
        return Ok(ApiResponse<List<StockReportSummaryDto>>.SuccessResponse(result));
    }

    // ── Admin: get report for a region ──────────────────────────────────────

    /// <summary>Get full stock report for a region (Admin)</summary>
    [HttpGet("admin/stock-reports/{regionId:guid}")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public async Task<IActionResult> GetByRegion(Guid regionId, CancellationToken ct)
    {
        var result = await _service.GetReportByRegionAsync(regionId, ct);
        return Ok(ApiResponse<StockReportDetailDto>.SuccessResponse(result));
    }

    // ── Admin: delete ────────────────────────────────────────────────────────

    /// <summary>Delete stock report for a region (Admin)</summary>
    [HttpDelete("admin/stock-reports/{regionId:guid}")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public async Task<IActionResult> Delete(Guid regionId, CancellationToken ct)
    {
        await _service.DeleteReportAsync(regionId, ct);
        return Ok(ApiResponse<string>.SuccessResponse("Report deleted."));
    }

    // ── Rep: get own region reports ──────────────────────────────────────────

    /// <summary>Get stock reports for all regions assigned to the current sales rep</summary>
    [HttpGet("rep/stock-reports")]
    [Authorize(Roles = "SalesRep")]
    public async Task<IActionResult> GetRepReports(CancellationToken ct)
    {
        var result = await _service.GetRepReportsAsync(GetUserId(), ct);
        return Ok(ApiResponse<List<StockReportSummaryDto>>.SuccessResponse(result));
    }

    // ── Rep: get a single assigned region's report ────────────────────────────

    /// <summary>Get the full stock report for one region — only if that region is assigned to the current sales rep</summary>
    [HttpGet("rep/stock-reports/{regionId:guid}")]
    [Authorize(Roles = "SalesRep")]
    public async Task<IActionResult> GetRepReportByRegion(Guid regionId, CancellationToken ct)
    {
        var result = await _service.GetRepReportByRegionAsync(GetUserId(), regionId, ct);
        return Ok(ApiResponse<StockReportDetailDto>.SuccessResponse(result));
    }
}
