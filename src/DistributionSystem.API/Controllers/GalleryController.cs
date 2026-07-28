using System.Security.Claims;
using DistributionSystem.Application.DTOs.Common;
using DistributionSystem.Application.DTOs.Gallery;
using DistributionSystem.Application.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DistributionSystem.API.Controllers;

[ApiController]
[Route("api")]
[Authorize]
[Produces("application/json")]
public class GalleryController : ControllerBase
{
    private readonly IGalleryService _galleryService;
    private readonly IFileStorageService _fileStorage;

    private static readonly string[] AllowedImageExtensions = [".jpg", ".jpeg", ".png", ".webp", ".gif"];
    private static readonly string[] AllowedImageTypes = ["image/jpeg", "image/png", "image/webp", "image/gif"];
    private const long MaxImageSizeBytes = 10 * 1024 * 1024;

    public GalleryController(IGalleryService galleryService, IFileStorageService fileStorage)
    {
        _galleryService = galleryService;
        _fileStorage = fileStorage;
    }

    private string GetUserName() =>
        User.FindFirstValue(ClaimTypes.Name)
        ?? User.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? "unknown";

    // ── Admin endpoints ────────────────────────────────────────────────

    /// <summary>Get all gallery items (Admin — includes inactive)</summary>
    [HttpGet("admin/gallery")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public async Task<IActionResult> GetAll(CancellationToken ct)
    {
        var items = await _galleryService.GetAllAsync(activeOnly: false, ct);
        return Ok(ApiResponse<List<GalleryItemDto>>.SuccessResponse(items));
    }

    /// <summary>Upload image and create gallery item (Admin)</summary>
    [HttpPost("admin/gallery")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> Create(
        [FromForm] string title,
        [FromForm] string? description,
        [FromForm] int displayOrder,
        IFormFile image,
        CancellationToken ct)
    {
        if (image == null || image.Length == 0)
            return BadRequest(ApiResponse<string>.ErrorResponse("Image file is required"));

        StoredFileResult saved;
        try
        {
            saved = await _fileStorage.SaveAsync(image, "gallery", FileAccessCategory.Public, AllowedImageExtensions, AllowedImageTypes, MaxImageSizeBytes, ct);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ApiResponse<string>.ErrorResponse(ex.Message));
        }

        var imageUrl = _fileStorage.GetPublicUrl(saved.StorageKey)!;
        var item = await _galleryService.CreateAsync(title, description, imageUrl, displayOrder, GetUserName(), ct);
        return Ok(ApiResponse<GalleryItemDto>.SuccessResponse(item, "Gallery item created"));
    }

    /// <summary>Update a gallery item (Admin)</summary>
    [HttpPut("admin/gallery/{id:guid}")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateGalleryItemRequest request, CancellationToken ct)
    {
        var item = await _galleryService.UpdateAsync(id, request.Title, request.Description, request.DisplayOrder, request.IsActive, GetUserName(), ct);
        return Ok(ApiResponse<GalleryItemDto>.SuccessResponse(item, "Gallery item updated"));
    }

    /// <summary>Delete a gallery item (Admin)</summary>
    [HttpDelete("admin/gallery/{id:guid}")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await _galleryService.DeleteAsync(id, ct);
        return Ok(ApiResponse<string>.SuccessResponse("Gallery item deleted"));
    }

    /// <summary>Add an extra image to a gallery item (Admin)</summary>
    [HttpPost("admin/gallery/{id:guid}/images")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> AddExtraImage(Guid id, IFormFile image, CancellationToken ct)
    {
        if (image == null || image.Length == 0)
            return BadRequest(ApiResponse<string>.ErrorResponse("Image file is required"));

        StoredFileResult saved;
        try
        {
            saved = await _fileStorage.SaveAsync(image, "gallery", FileAccessCategory.Public, AllowedImageExtensions, AllowedImageTypes, MaxImageSizeBytes, ct);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ApiResponse<string>.ErrorResponse(ex.Message));
        }

        var imageUrl = _fileStorage.GetPublicUrl(saved.StorageKey)!;
        var item = await _galleryService.AddExtraImageAsync(id, imageUrl, GetUserName(), ct);
        return Ok(ApiResponse<GalleryItemDto>.SuccessResponse(item, "Image added"));
    }

    /// <summary>Remove an extra image from a gallery item by index (Admin)</summary>
    [HttpDelete("admin/gallery/{id:guid}/images/{index:int}")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public async Task<IActionResult> RemoveExtraImage(Guid id, int index, CancellationToken ct)
    {
        var item = await _galleryService.RemoveExtraImageAsync(id, index, GetUserName(), ct);
        return Ok(ApiResponse<GalleryItemDto>.SuccessResponse(item, "Image removed"));
    }

    // ── Customer / Rep / Coordinator endpoints ─────────────────────────

    /// <summary>Get active gallery items (Customer/Rep/Coordinator)</summary>
    [HttpGet("customer/gallery")]
    [HttpGet("rep/gallery")]
    [HttpGet("coordinator/gallery")]
    public async Task<IActionResult> GetActive(CancellationToken ct)
    {
        var items = await _galleryService.GetAllAsync(activeOnly: true, ct);
        return Ok(ApiResponse<List<GalleryItemDto>>.SuccessResponse(items));
    }
}

public class UpdateGalleryItemRequest
{
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int DisplayOrder { get; set; }
    public bool IsActive { get; set; }
}
