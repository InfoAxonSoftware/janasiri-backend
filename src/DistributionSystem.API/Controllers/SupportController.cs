using System.Security.Claims;
using DistributionSystem.Application.DTOs.Common;
using DistributionSystem.Application.DTOs.Support;
using DistributionSystem.Application.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DistributionSystem.API.Controllers;

/// <summary>
/// Cart and support/complaint endpoints
/// </summary>
[ApiController]
[Route("api/customer")]
[Authorize(Roles = "Customer")]
[Produces("application/json")]
public class CartAndSupportController : ControllerBase
{
    private readonly ISupportService _supportService;

    public CartAndSupportController(ISupportService supportService)
    {
        _supportService = supportService;
    }

    private Guid GetUserId() => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    /// <summary>Submit a support complaint</summary>
    [HttpPost("support/complaints")]
    public async Task<IActionResult> SubmitComplaint([FromBody] CreateComplaintRequest request, CancellationToken ct)
    {
        var result = await _supportService.CustomerCreateComplaintAsync(GetUserId(), request, ct);
        return Ok(ApiResponse<ComplaintDto>.SuccessResponse(result, "Complaint submitted"));
    }

    /// <summary>Get my complaints</summary>
    [HttpGet("support/complaints")]
    public async Task<IActionResult> GetMyComplaints(CancellationToken ct)
    {
        var result = await _supportService.CustomerGetComplaintsAsync(GetUserId(), ct);
        return Ok(ApiResponse<List<ComplaintDto>>.SuccessResponse(result));
    }

    /// <summary>Get complaint messages</summary>
    [HttpGet("support/complaints/{id}/messages")]
    public async Task<IActionResult> GetMessages(Guid id, CancellationToken ct)
    {
        var result = await _supportService.GetMessagesAsync(GetUserId(), "Customer", id, ct);
        return Ok(ApiResponse<List<ComplaintMessageDto>>.SuccessResponse(result));
    }

    /// <summary>Send complaint message</summary>
    [HttpPost("support/complaints/{id}/messages")]
    public async Task<IActionResult> SendMessage(Guid id, [FromBody] SendComplaintMessageRequest request, CancellationToken ct)
    {
        var result = await _supportService.SendMessageAsync(GetUserId(), "Customer", id, request, ct);
        return Ok(ApiResponse<ComplaintMessageDto>.SuccessResponse(result, "Message sent"));
    }
}

/// <summary>
/// Sales Rep support/complaint endpoints
/// </summary>
[ApiController]
[Route("api/rep/support")]
[Authorize(Roles = "SalesRep")]
[Produces("application/json")]
public class RepSupportController : ControllerBase
{
    private readonly ISupportService _supportService;

    public RepSupportController(ISupportService supportService)
    {
        _supportService = supportService;
    }

    private Guid GetUserId() => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    /// <summary>Submit a support complaint as rep</summary>
    [HttpPost("complaints")]
    public async Task<IActionResult> SubmitComplaint([FromBody] CreateComplaintRequest request, CancellationToken ct)
    {
        var result = await _supportService.RepCreateComplaintAsync(GetUserId(), request, ct);
        return Ok(ApiResponse<ComplaintDto>.SuccessResponse(result, "Complaint submitted"));
    }

    /// <summary>Get rep complaints</summary>
    [HttpGet("complaints")]
    public async Task<IActionResult> GetMyComplaints(CancellationToken ct)
    {
        var result = await _supportService.RepGetComplaintsAsync(GetUserId(), ct);
        return Ok(ApiResponse<List<ComplaintDto>>.SuccessResponse(result));
    }

    /// <summary>Get complaint messages</summary>
    [HttpGet("complaints/{id}/messages")]
    public async Task<IActionResult> GetMessages(Guid id, CancellationToken ct)
    {
        var result = await _supportService.GetMessagesAsync(GetUserId(), "SalesRep", id, ct);
        return Ok(ApiResponse<List<ComplaintMessageDto>>.SuccessResponse(result));
    }

    /// <summary>Send complaint message</summary>
    [HttpPost("complaints/{id}/messages")]
    public async Task<IActionResult> SendMessage(Guid id, [FromBody] SendComplaintMessageRequest request, CancellationToken ct)
    {
        var result = await _supportService.SendMessageAsync(GetUserId(), "SalesRep", id, request, ct);
        return Ok(ApiResponse<ComplaintMessageDto>.SuccessResponse(result, "Message sent"));
    }
}

/// <summary>
/// Sales Coordinator support/complaint endpoints
/// </summary>
[ApiController]
[Route("api/coordinator/support")]
[Authorize(Roles = "SalesCoordinator")]
[Produces("application/json")]
public class CoordinatorSupportController : ControllerBase
{
    private readonly ISupportService _supportService;

    public CoordinatorSupportController(ISupportService supportService)
    {
        _supportService = supportService;
    }

    private Guid GetUserId() => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpPost("complaints")]
    public async Task<IActionResult> SubmitComplaint([FromBody] CreateComplaintRequest request, CancellationToken ct)
    {
        var result = await _supportService.CoordinatorCreateComplaintAsync(GetUserId(), request, ct);
        return Ok(ApiResponse<ComplaintDto>.SuccessResponse(result, "Complaint submitted"));
    }

    [HttpGet("complaints")]
    public async Task<IActionResult> GetMyComplaints(CancellationToken ct)
    {
        var result = await _supportService.CoordinatorGetComplaintsAsync(GetUserId(), ct);
        return Ok(ApiResponse<List<ComplaintDto>>.SuccessResponse(result));
    }

    [HttpGet("complaints/{id}/messages")]
    public async Task<IActionResult> GetMessages(Guid id, CancellationToken ct)
    {
        var result = await _supportService.GetMessagesAsync(GetUserId(), "SalesCoordinator", id, ct);
        return Ok(ApiResponse<List<ComplaintMessageDto>>.SuccessResponse(result));
    }

    [HttpPost("complaints/{id}/messages")]
    public async Task<IActionResult> SendMessage(Guid id, [FromBody] SendComplaintMessageRequest request, CancellationToken ct)
    {
        var result = await _supportService.SendMessageAsync(GetUserId(), "SalesCoordinator", id, request, ct);
        return Ok(ApiResponse<ComplaintMessageDto>.SuccessResponse(result, "Message sent"));
    }
}

/// <summary>
/// Admin support management
/// </summary>
[ApiController]
[Route("api/admin/support")]
[Authorize(Roles = "Admin,SuperAdmin")]
[Produces("application/json")]
public class AdminSupportController : ControllerBase
{
    private readonly ISupportService _supportService;

    public AdminSupportController(ISupportService supportService)
    {
        _supportService = supportService;
    }

    private Guid GetUserId() => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    /// <summary>Get all complaints</summary>
    [HttpGet("complaints")]
    public async Task<IActionResult> GetAllComplaints([FromQuery] int page = 1, [FromQuery] int pageSize = 20, [FromQuery] string? status = null, CancellationToken ct = default)
    {
        var result = await _supportService.AdminGetComplaintsAsync(page, pageSize, status, ct);
        return Ok(ApiResponse<PagedResult<ComplaintDto>>.SuccessResponse(result));
    }

    /// <summary>Update complaint status</summary>
    [HttpPut("complaints/{id}/status")]
    public async Task<IActionResult> UpdateComplaintStatus(Guid id, [FromBody] string status, CancellationToken ct)
    {
        var trimmed = status?.Trim().Trim('"') ?? string.Empty;
        var result = await _supportService.AdminUpdateStatusAsync(GetUserId(), id, trimmed, ct);
        return Ok(ApiResponse<ComplaintDto>.SuccessResponse(result, "Complaint status updated"));
    }

    [HttpGet("complaints/{id}/messages")]
    public async Task<IActionResult> GetMessages(Guid id, CancellationToken ct)
    {
        var result = await _supportService.GetMessagesAsync(GetUserId(), "Admin", id, ct);
        return Ok(ApiResponse<List<ComplaintMessageDto>>.SuccessResponse(result));
    }

    [HttpPost("complaints/{id}/messages")]
    public async Task<IActionResult> SendMessage(Guid id, [FromBody] SendComplaintMessageRequest request, CancellationToken ct)
    {
        var result = await _supportService.SendMessageAsync(GetUserId(), "Admin", id, request, ct);
        return Ok(ApiResponse<ComplaintMessageDto>.SuccessResponse(result, "Message sent"));
    }
}

