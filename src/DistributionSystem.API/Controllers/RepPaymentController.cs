using System.Security.Claims;
using DistributionSystem.Application.DTOs.Common;
using DistributionSystem.Application.DTOs.RepPayments;
using DistributionSystem.Application.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DistributionSystem.API.Controllers;

[ApiController]
[Route("api")]
[Authorize]
[Produces("application/json")]
public class RepPaymentController : ControllerBase
{
    private readonly IRepPaymentService _service;
    private readonly IFileStorageService _fileStorage;

    public RepPaymentController(IRepPaymentService service, IFileStorageService fileStorage)
    {
        _service = service;
        _fileStorage = fileStorage;
    }

    private Guid GetUserId() => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private string GetUserName() => User.FindFirstValue(ClaimTypes.Name) ?? User.FindFirstValue(ClaimTypes.Email) ?? "Unknown";

    /// <summary>
    /// Download a payment report's evidence image. Access is resolved through the owning payment
    /// record using the same scoping as the existing role-specific GetById endpoints: SalesRep
    /// only their own report, SalesCoordinator only reports from reps assigned to them, Admin/
    /// SuperAdmin any report.
    /// </summary>
    [HttpGet("rep-payments/{id:guid}/evidence")]
    [Authorize(Roles = "SalesRep,SalesCoordinator,Admin,SuperAdmin")]
    public async Task<IActionResult> GetEvidence(Guid id, CancellationToken ct)
    {
        if (User.IsInRole("Admin") || User.IsInRole("SuperAdmin"))
            await _service.GetByIdForAdminAsync(id, ct);
        else if (User.IsInRole("SalesCoordinator"))
            await _service.GetForCoordinatorByIdAsync(id, GetUserId(), ct);
        else
            await _service.GetForRepByIdAsync(id, GetUserId(), ct);
        // Each branch throws NotFoundException (-> 404) if the payment is out of the caller's scope.

        var storageKey = await _service.GetEvidenceStorageKeyAsync(id, ct);
        if (storageKey is null) return NotFound();

        var file = await _fileStorage.ReadAsync(storageKey, ct);
        if (file is null) return NotFound();

        Response.Headers.CacheControl = "no-store, private";
        return File(file.Content, file.ContentType, $"payment-evidence-{id}{Path.GetExtension(file.FileName)}");
    }

    // ── Rep ───────────────────────────────────────────────────────────────────

    [HttpPost("rep/payment-reports")]
    [Authorize(Roles = "SalesRep")]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> Create([FromForm] string customerName, [FromForm] string referenceNumber, [FromForm] decimal amount, [FromForm] IFormFile? image, CancellationToken ct)
    {
        var dto = new CreateRepPaymentDto { CustomerName = customerName, ReferenceNumber = referenceNumber, Amount = amount };
        var result = await _service.CreateAsync(GetUserId(), dto, image, ct);
        return Ok(ApiResponse<RepPaymentDto>.SuccessResponse(result, "Payment report submitted."));
    }

    [HttpGet("rep/payment-reports")]
    [Authorize(Roles = "SalesRep")]
    public async Task<IActionResult> GetRepPayments([FromQuery] RepPaymentQueryDto query, CancellationToken ct)
    {
        var trash = string.Equals(query.View, "trash", StringComparison.OrdinalIgnoreCase);
        var result = await _service.GetRepPaymentsAsync(GetUserId(), query, trash, ct);
        return Ok(ApiResponse<PagedResult<RepPaymentDto>>.SuccessResponse(result));
    }

    [HttpGet("rep/payment-reports/{id}")]
    [Authorize(Roles = "SalesRep")]
    public async Task<IActionResult> GetRepById(Guid id, CancellationToken ct)
    {
        var result = await _service.GetForRepByIdAsync(id, GetUserId(), ct);
        return Ok(ApiResponse<RepPaymentDto>.SuccessResponse(result));
    }

    [HttpDelete("rep/payment-reports/{id}")]
    [Authorize(Roles = "SalesRep")]
    public async Task<IActionResult> DeleteRep(Guid id, CancellationToken ct)
    {
        await _service.DeleteRepAsync(id, GetUserId(), ct);
        return Ok(ApiResponse<string>.SuccessResponse("Deleted."));
    }

    [HttpPut("rep/payment-reports/{id}/trash")]
    [Authorize(Roles = "SalesRep")]
    public async Task<IActionResult> RepTrash(Guid id, CancellationToken ct)
    {
        await _service.RepTrashAsync(id, GetUserId(), ct);
        return Ok(ApiResponse<string>.SuccessResponse("Moved to trash."));
    }

    [HttpPut("rep/payment-reports/{id}/restore")]
    [Authorize(Roles = "SalesRep")]
    public async Task<IActionResult> RepRestore(Guid id, CancellationToken ct)
    {
        await _service.RepRestoreAsync(id, GetUserId(), ct);
        return Ok(ApiResponse<string>.SuccessResponse("Restored."));
    }

    // ── Admin ─────────────────────────────────────────────────────────────────

    [HttpGet("admin/payment-reports")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public async Task<IActionResult> GetAll([FromQuery] RepPaymentQueryDto query, CancellationToken ct)
    {
        var result = await _service.GetAllAsync(query, trash: false, ct);
        return Ok(ApiResponse<PagedResult<RepPaymentDto>>.SuccessResponse(result));
    }

    [HttpGet("admin/payment-reports/trash")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public async Task<IActionResult> GetTrash([FromQuery] RepPaymentQueryDto query, CancellationToken ct)
    {
        query.View = "trash";
        var result = await _service.AdminGetTrashAsync(query, ct);
        return Ok(ApiResponse<PagedResult<RepPaymentDto>>.SuccessResponse(result));
    }

    [HttpGet("admin/payment-reports/rep-options")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public async Task<IActionResult> GetAdminRepOptions(CancellationToken ct)
    {
        var result = await _service.GetAdminRepOptionsAsync(ct);
        return Ok(ApiResponse<List<RepPaymentRepOptionDto>>.SuccessResponse(result));
    }

    [HttpGet("admin/payment-reports/{id}")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public async Task<IActionResult> GetByIdAdmin(Guid id, CancellationToken ct)
    {
        var result = await _service.GetByIdForAdminAsync(id, ct);
        return Ok(ApiResponse<RepPaymentDto>.SuccessResponse(result));
    }

    [HttpPut("admin/payment-reports/{id}/status")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public async Task<IActionResult> UpdateStatus(Guid id, [FromBody] UpdateRepPaymentStatusDto dto, CancellationToken ct)
    {
        var result = await _service.UpdateStatusAsync(id, dto, GetUserId(), GetUserName(), ct);
        return Ok(ApiResponse<RepPaymentDto>.SuccessResponse(result, "Status updated."));
    }

    [HttpDelete("admin/payment-reports/{id}")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public async Task<IActionResult> SoftDelete(Guid id, CancellationToken ct)
    {
        await _service.AdminSoftDeleteAsync(id, GetUserName(), ct);
        return Ok(ApiResponse<string>.SuccessResponse("Moved to trash."));
    }

    [HttpPut("admin/payment-reports/{id}/trash")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public async Task<IActionResult> AdminTrash(Guid id, CancellationToken ct)
    {
        await _service.AdminSoftDeleteAsync(id, GetUserName(), ct);
        return Ok(ApiResponse<string>.SuccessResponse("Moved to trash."));
    }

    [HttpPost("admin/payment-reports/{id}/restore")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public async Task<IActionResult> Restore(Guid id, CancellationToken ct)
    {
        await _service.AdminRestoreAsync(id, ct);
        return Ok(ApiResponse<string>.SuccessResponse("Restored."));
    }

    [HttpPut("admin/payment-reports/{id}/restore")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public async Task<IActionResult> AdminRestorePut(Guid id, CancellationToken ct)
    {
        await _service.AdminRestoreAsync(id, ct);
        return Ok(ApiResponse<string>.SuccessResponse("Restored."));
    }

    [HttpDelete("admin/payment-reports/{id}/permanent")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public async Task<IActionResult> HardDelete(Guid id, CancellationToken ct)
    {
        await _service.AdminHardDeleteAsync(id, ct);
        return Ok(ApiResponse<string>.SuccessResponse("Permanently deleted."));
    }

    [HttpPost("admin/payment-reports/bulk-delete")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public async Task<IActionResult> BulkDelete([FromBody] List<Guid> ids, CancellationToken ct)
    {
        await _service.BulkSoftDeleteAdminAsync(ids, GetUserName(), ct);
        return Ok(ApiResponse<string>.SuccessResponse($"{ids.Count} payment(s) moved to trash."));
    }

    [HttpPost("admin/payment-reports/bulk-status")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public async Task<IActionResult> BulkStatus([FromBody] BulkStatusRequest req, CancellationToken ct)
    {
        await _service.BulkUpdateStatusAsync(req.Ids, req.Status, GetUserName(), ct);
        return Ok(ApiResponse<string>.SuccessResponse($"{req.Ids.Count} payment(s) updated."));
    }

    // ── Coordinator ───────────────────────────────────────────────────────────

    [HttpGet("coordinator/payment-reports")]
    [Authorize(Roles = "SalesCoordinator")]
    public async Task<IActionResult> GetForCoordinator([FromQuery] RepPaymentQueryDto query, CancellationToken ct)
    {
        var trash = string.Equals(query.View, "trash", StringComparison.OrdinalIgnoreCase);
        var result = await _service.GetForCoordinatorAsync(GetUserId(), query, trash, ct);
        return Ok(ApiResponse<PagedResult<RepPaymentDto>>.SuccessResponse(result));
    }

    [HttpGet("coordinator/payment-reports/rep-options")]
    [Authorize(Roles = "SalesCoordinator")]
    public async Task<IActionResult> GetCoordinatorRepOptions(CancellationToken ct)
    {
        var result = await _service.GetCoordinatorRepOptionsAsync(GetUserId(), ct);
        return Ok(ApiResponse<List<RepPaymentRepOptionDto>>.SuccessResponse(result));
    }

    [HttpGet("coordinator/payment-reports/{id}")]
    [Authorize(Roles = "SalesCoordinator")]
    public async Task<IActionResult> GetForCoordinatorById(Guid id, CancellationToken ct)
    {
        var result = await _service.GetForCoordinatorByIdAsync(id, GetUserId(), ct);
        return Ok(ApiResponse<RepPaymentDto>.SuccessResponse(result));
    }

    [HttpPut("coordinator/payment-reports/{id}/status")]
    [Authorize(Roles = "SalesCoordinator")]
    public async Task<IActionResult> CoordinatorUpdateStatus(Guid id, [FromBody] UpdateRepPaymentStatusDto dto, CancellationToken ct)
    {
        var result = await _service.CoordinatorUpdateStatusAsync(id, dto, GetUserId(), ct);
        return Ok(ApiResponse<RepPaymentDto>.SuccessResponse(result, "Status updated."));
    }

    [HttpPut("coordinator/payment-reports/{id}/trash")]
    [Authorize(Roles = "SalesCoordinator")]
    public async Task<IActionResult> CoordinatorTrash(Guid id, CancellationToken ct)
    {
        await _service.CoordinatorTrashAsync(id, GetUserId(), ct);
        return Ok(ApiResponse<string>.SuccessResponse("Moved to trash."));
    }

    [HttpPut("coordinator/payment-reports/{id}/restore")]
    [Authorize(Roles = "SalesCoordinator")]
    public async Task<IActionResult> CoordinatorRestore(Guid id, CancellationToken ct)
    {
        await _service.CoordinatorRestoreAsync(id, GetUserId(), ct);
        return Ok(ApiResponse<string>.SuccessResponse("Restored."));
    }
}

public class BulkStatusRequest
{
    public List<Guid> Ids { get; set; } = [];
    public string Status { get; set; } = string.Empty;
}
