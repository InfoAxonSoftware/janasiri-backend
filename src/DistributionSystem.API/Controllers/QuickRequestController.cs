using System.Security.Claims;
using DistributionSystem.Application.DTOs.Common;
using DistributionSystem.Application.DTOs.Notification;
using DistributionSystem.Application.DTOs.QuickRequests;
using DistributionSystem.Application.Services.Interfaces;
using DistributionSystem.API.Hubs;
using DistributionSystem.Domain.Entities;
using DistributionSystem.Domain.Enums;
using DistributionSystem.Infrastructure.Data.Repositories.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace DistributionSystem.API.Controllers;

[ApiController]
[Route("api")]
[Authorize]
[Produces("application/json")]
public class QuickRequestController : ControllerBase
{
    private readonly IQuickRequestService _service;
    private readonly INotificationService _notificationService;
    private readonly IHubContext<NotificationHub> _notificationHub;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IFileStorageService _fileStorage;

    public QuickRequestController(
        IQuickRequestService service,
        INotificationService notificationService,
        IHubContext<NotificationHub> notificationHub,
        IUnitOfWork unitOfWork,
        IFileStorageService fileStorage)
    {
        _service = service;
        _notificationService = notificationService;
        _notificationHub = notificationHub;
        _unitOfWork = unitOfWork;
        _fileStorage = fileStorage;
    }

    private Guid GetUserId() => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private string GetUsername() => User.FindFirstValue(ClaimTypes.Name) ?? User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    /// <summary>
    /// Download one attachment image of a quick request. Access is resolved through the owning
    /// request record using the same scoping as the existing role-specific GetById endpoints:
    /// SalesRep only their own request, SalesCoordinator only requests from reps assigned to
    /// them, Admin/SuperAdmin any request.
    /// </summary>
    [HttpGet("quick-requests/{requestId:guid}/images/{imageId:guid}")]
    [Authorize(Roles = "SalesRep,SalesCoordinator,Admin,SuperAdmin")]
    public async Task<IActionResult> GetImage(Guid requestId, Guid imageId, CancellationToken ct)
    {
        if (User.IsInRole("Admin") || User.IsInRole("SuperAdmin"))
            await _service.GetByIdAsync(requestId, ct);
        else if (User.IsInRole("SalesCoordinator"))
            await _service.GetForCoordinatorByIdAsync(requestId, GetUserId(), ct);
        else
            await _service.GetRepRequestByIdAsync(requestId, GetUserId(), ct);
        // Each branch throws NotFoundException (-> 404) if the request is out of the caller's scope.

        var storageKey = await _service.GetImageStorageKeyAsync(requestId, imageId, ct);
        if (storageKey is null) return NotFound();

        var file = await _fileStorage.ReadAsync(storageKey, ct);
        if (file is null) return NotFound();

        Response.Headers.CacheControl = "no-store, private";
        return File(file.Content, file.ContentType, $"quick-request-{requestId}-{imageId}{Path.GetExtension(file.FileName)}");
    }

    // ── Rep: Create ──────────────────────────────────────────────────────────

    [HttpPost("rep/quick-requests")]
    [Authorize(Roles = "SalesRep")]
    public async Task<IActionResult> Create([FromBody] CreateQuickRequestDto dto, CancellationToken ct)
    {
        var result = await _service.CreateAsync(GetUserId(), dto, ct);

        // Notify all admins (DB + real-time SignalR)
        var typeLabel = result.Type == "Order" ? "Quick Order" : "Quick Quotation";
        var repName = result.RepName ?? "Sales Rep";
        await _notificationService.SendToRoleAsync(new BroadcastNotificationRequest
        {
            Role = "Admin",
            Title = $"New {typeLabel}",
            Message = $"New {typeLabel.ToLower()} #{result.RequestNumber} submitted by {repName}",
            Type = "NewQuickRequest",
        }, ct);
        await _notificationHub.Clients.Group("role_Admin").SendAsync("NewQuickRequest", new
        {
            id = result.Id,
            requestNumber = result.RequestNumber,
            type = result.Type,
            customerName = result.CustomerName,
            repName,
        }, ct);

        // Notify each coordinator assigned to this rep (DB + real-time SignalR)
        var repProfile = await _unitOfWork.Repository<SalesRepProfile>()
            .Query()
            .Include(r => r.Coordinators).ThenInclude(rc => rc.Coordinator).ThenInclude(c => c.User)
            .FirstOrDefaultAsync(r => r.UserId == GetUserId(), ct);
        if (repProfile?.Coordinators != null)
        {
            foreach (var rc in repProfile.Coordinators)
            {
                var coordUserId = rc.Coordinator?.UserId;
                if (coordUserId.HasValue)
                {
                    await _notificationService.SendNotificationAsync(
                        coordUserId.Value,
                        Domain.Enums.NotificationType.NewOrder,
                        $"New {typeLabel}",
                        $"New {typeLabel.ToLower()} #{result.RequestNumber} for {result.CustomerName} by {repName}.",
                        ct);
                    await _notificationHub.Clients.Group($"user_{coordUserId.Value}")
                        .SendAsync("NewQuickRequest", new
                        {
                            id = result.Id,
                            requestNumber = result.RequestNumber,
                            type = result.Type,
                            customerName = result.CustomerName,
                            repName,
                            channel = "coordinator",
                        }, ct);
                }
            }
        }

        return Ok(ApiResponse<QuickRequestDto>.SuccessResponse(result, "Quick request submitted."));
    }

    // ── Rep: Upload images ───────────────────────────────────────────────────

    [HttpPost("rep/quick-requests/{id:guid}/images")]
    [Authorize(Roles = "SalesRep")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(50 * 1024 * 1024)] // 50 MB total for multiple images
    public async Task<IActionResult> UploadImages(Guid id, [FromForm] List<IFormFile> images, CancellationToken ct)
    {
        if (images == null || images.Count == 0)
            return BadRequest(ApiResponse<string>.ErrorResponse("No images provided."));

        var result = await _service.AddImagesAsync(id, GetUserId(), images, ct);

        // Tell admins to re-fetch so the newly uploaded images appear in the table
        await _notificationHub.Clients.Group("role_Admin").SendAsync("QuickRequestUpdated", new
        {
            id = result.Id,
            type = result.Type,
        }, ct);

        // Tell coordinators assigned to this rep to re-fetch
        var repProfileImg = await _unitOfWork.Repository<SalesRepProfile>()
            .Query()
            .Include(r => r.Coordinators).ThenInclude(rc => rc.Coordinator).ThenInclude(c => c.User)
            .FirstOrDefaultAsync(r => r.UserId == GetUserId(), ct);
        if (repProfileImg?.Coordinators != null)
        {
            foreach (var rc in repProfileImg.Coordinators)
            {
                var coordUserId = rc.Coordinator?.UserId;
                if (coordUserId.HasValue)
                    await _notificationHub.Clients.Group($"user_{coordUserId.Value}")
                        .SendAsync("QuickRequestUpdated", new { id = result.Id, type = result.Type }, ct);
            }
        }

        return Ok(ApiResponse<QuickRequestDto>.SuccessResponse(result, "Images uploaded."));
    }

    // ── Rep: List ────────────────────────────────────────────────────────────

    [HttpGet("rep/quick-requests")]
    [Authorize(Roles = "SalesRep")]
    public async Task<IActionResult> RepGetAll([FromQuery] string? type, CancellationToken ct)
    {
        var result = await _service.GetRepRequestsAsync(GetUserId(), type, ct);
        return Ok(ApiResponse<List<QuickRequestDto>>.SuccessResponse(result));
    }

    [HttpGet("rep/quick-requests/{id:guid}")]
    [Authorize(Roles = "SalesRep")]
    public async Task<IActionResult> RepGetById(Guid id, CancellationToken ct)
    {
        var result = await _service.GetRepRequestByIdAsync(id, GetUserId(), ct);
        return Ok(ApiResponse<QuickRequestDto>.SuccessResponse(result));
    }

    // ── Admin: Create ────────────────────────────────────────────────────────────

    [HttpPost("admin/quick-requests")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public async Task<IActionResult> AdminCreate(
        [FromBody] CreateQuickRequestDto dto,
        CancellationToken ct)
    {
        var result = await _service.CreateAdminAsync(
            GetUserId(),
            GetUsername(),
            dto,
            ct);

        return Ok(
            ApiResponse<QuickRequestDto>.SuccessResponse(
                result,
                "Quick order created."));
    }

    [HttpPost("admin/quick-requests/{id:guid}/images")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(50 * 1024 * 1024)]
    public async Task<IActionResult> AdminUploadImages(
        Guid id,
        [FromForm] List<IFormFile> images,
        CancellationToken ct)
    {
        if (images == null || images.Count == 0)
            return BadRequest(
                ApiResponse<string>.ErrorResponse(
                    "No images provided."));

        var result = await _service.AddAdminImagesAsync(
            id,
            images,
            ct);

        return Ok(
            ApiResponse<QuickRequestDto>.SuccessResponse(
                result,
                "Images uploaded."));
    }

    // ── Admin: List & detail ─────────────────────────────────────────────────

    [HttpGet("admin/quick-requests")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public async Task<IActionResult> AdminGetAll([FromQuery] string? type, [FromQuery] string? status, CancellationToken ct)
    {
        var result = await _service.GetAllAsync(type, status, ct);
        return Ok(ApiResponse<List<QuickRequestDto>>.SuccessResponse(result));
    }

    [HttpGet("admin/quick-requests/{id:guid}")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public async Task<IActionResult> AdminGetById(Guid id, CancellationToken ct)
    {
        var result = await _service.GetByIdAsync(id, ct);
        return Ok(ApiResponse<QuickRequestDto>.SuccessResponse(result));
    }

    // ── Admin: Update status ─────────────────────────────────────────────────

    [HttpPut("admin/quick-requests/{id:guid}/status")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public async Task<IActionResult> UpdateStatus(Guid id, [FromBody] UpdateQuickRequestStatusDto dto, CancellationToken ct)
    {
        var result = await _service.UpdateStatusAsync(id, dto, GetUsername(), ct);

        // Notify the rep of the status change
        var repUserId = await _unitOfWork.Repository<SalesRepProfile>().Query()
            .Where(r => r.Id == result.RepId)
            .Select(r => r.UserId)
            .FirstOrDefaultAsync(ct);

        if (repUserId != Guid.Empty)
        {
            var label = result.Type == "Order" ? "Quick Order" : "Quick Quotation";
            await _notificationService.SendNotificationAsync(
                repUserId,
                NotificationType.General,
                $"{label} Status Updated",
                $"Your {label.ToLower()} #{result.RequestNumber} status changed to {result.Status}.",
                ct);
        }

        return Ok(ApiResponse<QuickRequestDto>.SuccessResponse(result, "Status updated."));
    }

    [HttpDelete("admin/quick-requests/{id:guid}")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public async Task<IActionResult> AdminSoftDeleteQuickRequest(Guid id, CancellationToken ct)
    {
        await _service.AdminSoftDeleteAsync(id, GetUsername(), ct);
        return Ok(ApiResponse<string>.SuccessResponse("Moved to trash.", "Quick request moved to trash."));
    }

    [HttpGet("admin/quick-requests/trash")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public async Task<IActionResult> AdminGetTrash([FromQuery] string? type, CancellationToken ct)
    {
        var result = await _service.AdminGetTrashAsync(type, ct);
        return Ok(ApiResponse<List<QuickRequestDto>>.SuccessResponse(result));
    }

    [HttpPost("admin/quick-requests/{id:guid}/restore")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public async Task<IActionResult> AdminRestoreQuickRequest(Guid id, CancellationToken ct)
    {
        await _service.AdminRestoreAsync(id, ct);
        return Ok(ApiResponse<string>.SuccessResponse("Restored.", "Quick request restored."));
    }

    // ── Rep: Trash ───────────────────────────────────────────────────────────

    [HttpDelete("rep/quick-requests/{id:guid}")]
    [Authorize(Roles = "SalesRep")]
    public async Task<IActionResult> RepDeleteQuickRequest(Guid id, CancellationToken ct)
    {
        await _service.RepSoftDeleteAsync(id, GetUserId(), ct);
        return Ok(ApiResponse<string>.SuccessResponse("Moved to trash.", "Quick request moved to trash."));
    }

    [HttpGet("rep/quick-requests/trash")]
    [Authorize(Roles = "SalesRep")]
    public async Task<IActionResult> RepGetTrash([FromQuery] string? type, CancellationToken ct)
    {
        var result = await _service.RepGetTrashAsync(GetUserId(), type, ct);
        return Ok(ApiResponse<List<QuickRequestDto>>.SuccessResponse(result));
    }

    [HttpPost("rep/quick-requests/{id:guid}/restore")]
    [Authorize(Roles = "SalesRep")]
    public async Task<IActionResult> RepRestoreQuickRequest(Guid id, CancellationToken ct)
    {
        await _service.RepRestoreAsync(id, GetUserId(), ct);
        return Ok(ApiResponse<string>.SuccessResponse("Restored.", "Quick request restored."));
    }

    // ── Coordinator ───────────────────────────────────────────────────────────

    [HttpDelete("coordinator/quick-requests/{id:guid}")]
    [Authorize(Roles = "SalesCoordinator")]
    public async Task<IActionResult> CoordinatorDeleteQuickRequest(Guid id, CancellationToken ct)
    {
        await _service.CoordinatorSoftDeleteAsync(id, GetUserId(), ct);
        return Ok(ApiResponse<string>.SuccessResponse("Moved to trash.", "Quick request moved to trash."));
    }

    [HttpPut("coordinator/quick-requests/{id:guid}/status")]
    [Authorize(Roles = "SalesCoordinator")]
    public async Task<IActionResult> CoordinatorUpdateStatus(Guid id, [FromBody] UpdateQuickRequestStatusDto dto, CancellationToken ct)
    {
        var result = await _service.CoordinatorUpdateStatusAsync(id, dto, GetUserId(), ct);
        return Ok(ApiResponse<QuickRequestDto>.SuccessResponse(result, "Status updated."));
    }

    [HttpGet("coordinator/quick-requests/trash")]
    [Authorize(Roles = "SalesCoordinator")]
    public async Task<IActionResult> CoordinatorGetTrash([FromQuery] string? type, CancellationToken ct)
    {
        var result = await _service.CoordinatorGetTrashAsync(GetUserId(), type, ct);
        return Ok(ApiResponse<List<QuickRequestDto>>.SuccessResponse(result));
    }

    [HttpPost("coordinator/quick-requests/{id:guid}/restore")]
    [Authorize(Roles = "SalesCoordinator")]
    public async Task<IActionResult> CoordinatorRestoreQuickRequest(Guid id, CancellationToken ct)
    {
        await _service.CoordinatorRestoreAsync(id, GetUserId(), ct);
        return Ok(ApiResponse<string>.SuccessResponse("Restored.", "Quick request restored."));
    }

    [HttpGet("coordinator/quick-requests")]
    [Authorize(Roles = "SalesCoordinator")]
    public async Task<IActionResult> CoordinatorGetAll([FromQuery] string? type, [FromQuery] string? status, CancellationToken ct)
    {
        var result = await _service.GetForCoordinatorAsync(GetUserId(), type, status, ct);
        return Ok(ApiResponse<List<QuickRequestDto>>.SuccessResponse(result));
    }
}
