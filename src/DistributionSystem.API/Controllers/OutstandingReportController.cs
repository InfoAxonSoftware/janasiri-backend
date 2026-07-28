using System.Security.Claims;
using DistributionSystem.Application.DTOs.Common;
using DistributionSystem.Application.DTOs.Outstanding;
using DistributionSystem.Application.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MiniExcelLibs;

namespace DistributionSystem.API.Controllers;

[ApiController]
[Route("api")]
[Authorize]
[Produces("application/json")]
public class OutstandingReportController : ControllerBase
{
    private readonly IOutstandingReportService _service;
    public OutstandingReportController(IOutstandingReportService service) => _service = service;

    private Guid GetUserId() => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private string GetUsername() => User.FindFirstValue(ClaimTypes.Name) ?? User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    // ── Admin: upload ────────────────────────────────────────────────────────

    /// <summary>Upload an outstanding Excel file for a region (Admin)</summary>
    [HttpPost("admin/outstanding-reports/upload")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    [RequestSizeLimit(20 * 1024 * 1024)] // 20 MB
    public async Task<IActionResult> Upload(
        [FromForm] Guid regionId,
        [FromForm] DateTime? reportDate,
        IFormFile file,
        CancellationToken ct)
    {
        if (file == null || file.Length == 0)
            return BadRequest(ApiResponse<string>.ErrorResponse("No file uploaded."));

        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (ext is not ".xlsx" and not ".xls")
            return BadRequest(ApiResponse<string>.ErrorResponse("Only .xlsx and .xls files are supported."));

        var result = await _service.UploadReportAsync(regionId, reportDate, file, GetUsername(), ct);
        return Ok(ApiResponse<OutstandingReportDto>.SuccessResponse(result, "Report uploaded and processed."));
    }

    // ── Admin: chunk upload — step 1 ────────────────────────────────────────

    /// <summary>Create (or replace) the report header, returning a reportId for subsequent entry chunks.</summary>
    [HttpPost("admin/outstanding-reports/start-upload")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public async Task<IActionResult> StartUpload([FromBody] StartOutstandingUploadRequest req, CancellationToken ct)
    {
        var result = await _service.StartUploadAsync(req.RegionId, req.ReportDate, GetUsername(), ct);
        return Ok(ApiResponse<StartOutstandingUploadResponse>.SuccessResponse(result, "Upload session started."));
    }

    // ── Admin: chunk upload — step 2 ────────────────────────────────────────

    /// <summary>Append a batch of parsed entries to an in-progress upload.</summary>
    [HttpPost("admin/outstanding-reports/{reportId:guid}/entries")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public async Task<IActionResult> AppendEntries(Guid reportId, [FromBody] AppendEntriesRequest req, CancellationToken ct)
    {
        await _service.AppendEntriesAsync(reportId, req.Entries, ct);
        return Ok(ApiResponse<string>.SuccessResponse($"{req.Entries.Count} entries appended."));
    }

    // ── Admin: list all regions' reports ────────────────────────────────────

    /// <summary>Get summary of all uploaded outstanding reports (Admin)</summary>
    [HttpGet("admin/outstanding-reports")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public async Task<IActionResult> GetAll(CancellationToken ct)
    {
        var result = await _service.GetAllReportsAsync(ct);
        return Ok(ApiResponse<List<OutstandingReportDto>>.SuccessResponse(result));
    }

    // ── Admin: get entries for a region ─────────────────────────────────────

    /// <summary>Get full outstanding report entries for a region (Admin)</summary>
    [HttpGet("admin/outstanding-reports/{regionId:guid}")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public async Task<IActionResult> GetByRegion(Guid regionId, CancellationToken ct)
    {
        var result = await _service.GetReportByRegionAsync(regionId, ct);
        return Ok(ApiResponse<OutstandingReportDetailDto>.SuccessResponse(result));
    }

    // ── Admin: delete ────────────────────────────────────────────────────────

    /// <summary>Delete outstanding report for a region (Admin)</summary>
    [HttpDelete("admin/outstanding-reports/{regionId:guid}")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public async Task<IActionResult> Delete(Guid regionId, CancellationToken ct)
    {
        await _service.DeleteReportAsync(regionId, ct);
        return Ok(ApiResponse<string>.SuccessResponse("Report deleted."));
    }

    // ── Admin: inspect Excel column headers ─────────────────────────────────

    /// <summary>Returns the column headers found in an Excel file without saving — use for debugging column mapping</summary>
    [HttpPost("admin/outstanding-reports/inspect-columns")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    [RequestSizeLimit(20 * 1024 * 1024)]
    public IActionResult InspectColumns(IFormFile file)
    {
        if (file == null || file.Length == 0)
            return BadRequest(ApiResponse<string>.ErrorResponse("No file uploaded."));

        using var stream = file.OpenReadStream();
        var firstRow = stream.Query(useHeaderRow: true).Cast<IDictionary<string, object?>>().FirstOrDefault();
        if (firstRow == null)
            return Ok(ApiResponse<List<string>>.SuccessResponse([], "File is empty."));

        var columns = firstRow.Keys.Select(k => $"\"{k}\"").ToList();
        return Ok(ApiResponse<List<string>>.SuccessResponse(columns, $"Found {columns.Count} columns."));
    }

    // ── Rep: get own region reports ──────────────────────────────────────────

    /// <summary>Get outstanding reports for all regions assigned to the current sales rep</summary>
    [HttpGet("rep/outstanding-reports")]
    [Authorize(Roles = "SalesRep")]
    public async Task<IActionResult> GetRepReports(CancellationToken ct)
    {
        var result = await _service.GetRepReportsAsync(GetUserId(), ct);
        return Ok(ApiResponse<List<OutstandingReportDetailDto>>.SuccessResponse(result));
    }
}
