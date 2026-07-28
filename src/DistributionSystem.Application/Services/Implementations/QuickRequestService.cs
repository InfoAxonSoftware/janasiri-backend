using DistributionSystem.Application.DTOs.QuickRequests;
using DistributionSystem.Application.Exceptions;
using DistributionSystem.Application.Services.Interfaces;
using DistributionSystem.Domain.Entities;
using DistributionSystem.Domain.Enums;
using DistributionSystem.Infrastructure.Data.Repositories.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace DistributionSystem.Application.Services.Implementations;

public class QuickRequestService : IQuickRequestService
{
    private readonly IUnitOfWork _uow;
    private readonly IFileStorageService _fileStorage;

    // Unambiguous alphanumeric chars (no 0/O/1/I/L confusion)
    private static readonly char[] _numberChars =
        "ABCDEFGHJKLMNPQRSTUVWXYZ23456789".ToCharArray();

    private const long MaxImageSize = 5 * 1024 * 1024;

    private static readonly HashSet<string> AllowedImageTypes = new(StringComparer.OrdinalIgnoreCase)
        { "image/jpeg", "image/png", "image/webp" };

    private static readonly HashSet<string> AllowedImageExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".jpg", ".jpeg", ".png", ".webp" };

    public QuickRequestService(IUnitOfWork uow, IFileStorageService fileStorage)
    {
        _uow = uow;
        _fileStorage = fileStorage;
    }

    /// <summary>
    /// Generates a random unique request number like QO-X4K9P2A7.
    /// Retries until the number doesn't already exist in the database.
    /// </summary>
    private async Task<string> GenerateUniqueNumberAsync(QuickRequestType type, CancellationToken ct)
    {
        var prefix = type switch
        {
            QuickRequestType.Order => "QO",
            QuickRequestType.Quotation => "QQ",
            _ => throw new BusinessException("Invalid quick request type.", "INVALID_QUICK_REQUEST_TYPE"),
        };
        string number;
        do
        {
            var random = new string(Enumerable.Range(0, 8)
                .Select(_ => _numberChars[Random.Shared.Next(_numberChars.Length)])
                .ToArray());
            number = $"{prefix}-{random}";
        } while (await _uow.Repository<QuickRequest>().Query()
            .AnyAsync(q => q.RequestNumber == number, ct));
        return number;
    }

    // ── Rep ──────────────────────────────────────────────────────────────────

    public async Task<QuickRequestDto> CreateAsync(
        Guid repUserId,
        CreateQuickRequestDto dto,
        CancellationToken ct = default)
    {
        var rep = await _uow.Repository<SalesRepProfile>().Query()
            .FirstOrDefaultAsync(r => r.UserId == repUserId, ct)
            ?? throw new NotFoundException("Sales rep profile", repUserId);

        if (!Enum.TryParse<QuickRequestType>(dto.Type, ignoreCase: true, out var type))
            throw new BusinessException("Select Order or Quotation.", "INVALID_QUICK_REQUEST_TYPE");
        if (string.IsNullOrWhiteSpace(dto.CustomerName))
            throw new BusinessException("Customer name is required.", "QUICK_REQUEST_CUSTOMER_REQUIRED");
        if (string.IsNullOrWhiteSpace(dto.Details))
            throw new BusinessException("Add at least one item.", "QUICK_REQUEST_DETAILS_REQUIRED");

        var number = await GenerateUniqueNumberAsync(type, ct);

        var request = new QuickRequest
        {
            RequestNumber = number,
            Type = type,
            CustomerName = dto.CustomerName.Trim(),
            Details = dto.Details.Trim(),
            RepId = rep.Id,
            CreatedBy = rep.FullName,
        };

        await _uow.Repository<QuickRequest>().AddAsync(request, ct);
        await _uow.SaveChangesAsync(ct);

        return MapToDto(request, rep.FullName);
    }

    public async Task<QuickRequestDto> AddImagesAsync(
        Guid requestId, Guid repUserId, IList<IFormFile> images, CancellationToken ct = default)
    {
        var rep = await _uow.Repository<SalesRepProfile>().Query()
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.UserId == repUserId, ct)
            ?? throw new NotFoundException("Sales rep profile", repUserId);

        // Verify ownership without tracking the parent entity to avoid spurious UPDATE
        var exists = await _uow.Repository<QuickRequest>().Query()
            .AsNoTracking()
            .AnyAsync(r => r.Id == requestId && r.RepId == rep.Id, ct);
        if (!exists) throw new NotFoundException("Quick request", requestId);

        var imageEntities = new List<QuickRequestImage>();

        foreach (var image in images)
        {
            if (image.Length == 0) continue;
            ValidateImage(image);
            imageEntities.Add(await SaveImageAsync(requestId, image, ct));
        }

        if (imageEntities.Count > 0)
        {
            await _uow.Repository<QuickRequestImage>().AddRangeAsync(imageEntities, ct);
            await _uow.SaveChangesAsync(ct);
        }

        // Return the updated request
        var request = await _uow.Repository<QuickRequest>().Query()
            .AsNoTracking()
            .Include(r => r.Images)
            .Include(r => r.Rep)
            .FirstOrDefaultAsync(r => r.Id == requestId, ct);

        return MapToDto(request!, request!.Rep?.FullName ?? string.Empty);
    }

    public async Task<List<QuickRequestDto>> GetRepRequestsAsync(
        Guid repUserId, string? type = null, CancellationToken ct = default)
    {
        var rep = await _uow.Repository<SalesRepProfile>().Query()
            .FirstOrDefaultAsync(r => r.UserId == repUserId, ct)
            ?? throw new NotFoundException("Sales rep profile", repUserId);

        var query = _uow.Repository<QuickRequest>().Query()
            .Include(r => r.Images)
            .Include(r => r.Rep)
            .Where(r => r.RepId == rep.Id && !r.IsDeletedByRep && !r.IsPurgedByRep);

        if (!string.IsNullOrEmpty(type) && Enum.TryParse<QuickRequestType>(type, ignoreCase: true, out var t))
            query = query.Where(r => r.Type == t);

        var list = await query.OrderByDescending(r => r.CreatedAt).ToListAsync(ct);
        return list.Select(r => MapToDto(r, r.Rep?.FullName ?? string.Empty)).ToList();
    }

    public async Task<QuickRequestDto> GetRepRequestByIdAsync(
        Guid requestId, Guid repUserId, CancellationToken ct = default)
    {
        var rep = await _uow.Repository<SalesRepProfile>().Query()
            .FirstOrDefaultAsync(r => r.UserId == repUserId, ct)
            ?? throw new NotFoundException("Sales rep profile", repUserId);

        var request = await _uow.Repository<QuickRequest>().Query()
            .Include(r => r.Images)
            .Include(r => r.Rep)
            .FirstOrDefaultAsync(r => r.Id == requestId && r.RepId == rep.Id, ct)
            ?? throw new NotFoundException("Quick request", requestId);

        return MapToDto(request, request.Rep?.FullName ?? string.Empty);
    }

    // ── Admin ─────────────────────────────────────────────────────────────────

    public async Task<List<QuickRequestDto>> GetAllAsync(
        string? type = null, string? status = null, CancellationToken ct = default)
    {
        var query = _uow.Repository<QuickRequest>().Query()
            .Include(r => r.Images)
            .Include(r => r.Rep)
            .Where(r => !r.IsDeletedByAdmin && !r.IsPurgedByAdmin)
            .AsQueryable();

        if (!string.IsNullOrEmpty(type) && Enum.TryParse<QuickRequestType>(type, ignoreCase: true, out var t))
            query = query.Where(r => r.Type == t);

        if (!string.IsNullOrEmpty(status) && Enum.TryParse<QuickRequestStatus>(status, ignoreCase: true, out var s))
            query = query.Where(r => r.Status == s);

        var list = await query.OrderByDescending(r => r.CreatedAt).ToListAsync(ct);
        return list.Select(r => MapToDto(r, r.Rep?.FullName ?? string.Empty)).ToList();
    }

    public async Task<QuickRequestDto> GetByIdAsync(Guid requestId, CancellationToken ct = default)
    {
        var request = await _uow.Repository<QuickRequest>().Query()
            .Include(r => r.Images)
            .Include(r => r.Rep)
            .FirstOrDefaultAsync(r => r.Id == requestId, ct)
            ?? throw new NotFoundException("Quick request", requestId);

        return MapToDto(request, request.Rep?.FullName ?? string.Empty);
    }

    /// <summary>Coordinator-scoped variant of <see cref="GetByIdAsync"/> — throws NotFoundException if the request's rep isn't assigned to this coordinator.</summary>
    public async Task<QuickRequestDto> GetForCoordinatorByIdAsync(Guid requestId, Guid coordinatorUserId, CancellationToken ct = default)
    {
        var coord = await _uow.Repository<CoordinatorProfile>().Query()
            .FirstOrDefaultAsync(c => c.UserId == coordinatorUserId, ct)
            ?? throw new NotFoundException("Coordinator profile", coordinatorUserId);

        var repIds = await _uow.Repository<RepCoordinator>().Query()
            .Where(rc => rc.CoordinatorId == coord.Id)
            .Select(rc => rc.RepId)
            .ToListAsync(ct);

        var request = await _uow.Repository<QuickRequest>().Query()
            .Include(r => r.Images)
            .Include(r => r.Rep)
            .FirstOrDefaultAsync(r => r.Id == requestId && repIds.Contains(r.RepId), ct)
            ?? throw new NotFoundException("Quick request", requestId);

        return MapToDto(request, request.Rep?.FullName ?? string.Empty);
    }

    public async Task<QuickRequestDto> UpdateStatusAsync(
        Guid requestId, UpdateQuickRequestStatusDto dto, string updatedBy, CancellationToken ct = default)
    {
        var request = await _uow.Repository<QuickRequest>().Query()
            .Include(r => r.Images)
            .Include(r => r.Rep)
            .FirstOrDefaultAsync(r => r.Id == requestId, ct)
            ?? throw new NotFoundException("Quick request", requestId);

        if (!Enum.TryParse<QuickRequestStatus>(dto.Status, ignoreCase: true, out var newStatus))
            throw new ArgumentException($"Invalid status '{dto.Status}'.");

        request.Status = newStatus;
        request.AdminNotes = dto.AdminNotes;
        request.UpdatedAt = DateTime.UtcNow;
        request.UpdatedBy = updatedBy;

        await _uow.SaveChangesAsync(ct);

        return MapToDto(request, request.Rep?.FullName ?? string.Empty);
    }

    public async Task DeleteAsync(Guid requestId, CancellationToken ct = default)
    {
        var request = await _uow.Repository<QuickRequest>().Query()
            .Include(r => r.Images)
            .FirstOrDefaultAsync(r => r.Id == requestId, ct)
            ?? throw new NotFoundException("Quick request", requestId);

        // Delete image files from disk
        foreach (var img in request.Images)
            _fileStorage.Delete(img.ImageUrl);

        _uow.Repository<QuickRequest>().Remove(request);
        await _uow.SaveChangesAsync(ct);
    }

    // ── Admin soft-delete / trash ─────────────────────────────────────────────

    public async Task AdminSoftDeleteAsync(Guid requestId, string deletedBy, CancellationToken ct = default)
    {
        var request = await _uow.Repository<QuickRequest>().Query()
            .FirstOrDefaultAsync(r => r.Id == requestId && !r.IsDeletedByAdmin && !r.IsPurgedByAdmin, ct)
            ?? throw new NotFoundException("Quick request", requestId);

        request.IsDeletedByAdmin = true;
        request.AdminDeletedAt = DateTime.UtcNow;
        request.UpdatedBy = deletedBy;
        await _uow.SaveChangesAsync(ct);
    }

    public async Task<List<QuickRequestDto>> AdminGetTrashAsync(string? type = null, CancellationToken ct = default)
    {
        var query = _uow.Repository<QuickRequest>().Query()
            .Include(r => r.Images).Include(r => r.Rep)
            .Where(r => r.IsDeletedByAdmin && !r.IsPurgedByAdmin);

        if (!string.IsNullOrEmpty(type) && Enum.TryParse<QuickRequestType>(type, ignoreCase: true, out var t))
            query = query.Where(r => r.Type == t);

        var list = await query.OrderByDescending(r => r.AdminDeletedAt).ToListAsync(ct);
        return list.Select(r => MapToDto(r, r.Rep?.FullName ?? string.Empty)).ToList();
    }

    public async Task AdminRestoreAsync(Guid requestId, CancellationToken ct = default)
    {
        var request = await _uow.Repository<QuickRequest>().Query()
            .FirstOrDefaultAsync(r => r.Id == requestId && r.IsDeletedByAdmin && !r.IsPurgedByAdmin, ct)
            ?? throw new NotFoundException("Quick request", requestId);

        request.IsDeletedByAdmin = false;
        request.AdminDeletedAt = null;
        await _uow.SaveChangesAsync(ct);
    }

    // ── Rep soft-delete / trash ────────────────────────────────────────────────

    public async Task RepSoftDeleteAsync(Guid requestId, Guid repUserId, CancellationToken ct = default)
    {
        var rep = await _uow.Repository<SalesRepProfile>().Query()
            .FirstOrDefaultAsync(r => r.UserId == repUserId, ct)
            ?? throw new NotFoundException("Sales rep profile", repUserId);

        var request = await _uow.Repository<QuickRequest>().Query()
            .FirstOrDefaultAsync(r => r.Id == requestId && r.RepId == rep.Id && !r.IsDeletedByRep && !r.IsPurgedByRep, ct)
            ?? throw new NotFoundException("Quick request", requestId);

        request.IsDeletedByRep = true;
        request.RepDeletedAt = DateTime.UtcNow;
        await _uow.SaveChangesAsync(ct);
    }

    public async Task<List<QuickRequestDto>> RepGetTrashAsync(Guid repUserId, string? type = null, CancellationToken ct = default)
    {
        var rep = await _uow.Repository<SalesRepProfile>().Query()
            .FirstOrDefaultAsync(r => r.UserId == repUserId, ct)
            ?? throw new NotFoundException("Sales rep profile", repUserId);

        var query = _uow.Repository<QuickRequest>().Query()
            .Include(r => r.Images).Include(r => r.Rep)
            .Where(r => r.RepId == rep.Id && r.IsDeletedByRep && !r.IsPurgedByRep);

        if (!string.IsNullOrEmpty(type) && Enum.TryParse<QuickRequestType>(type, ignoreCase: true, out var t))
            query = query.Where(r => r.Type == t);

        var list = await query.OrderByDescending(r => r.RepDeletedAt).ToListAsync(ct);
        return list.Select(r => MapToDto(r, r.Rep?.FullName ?? string.Empty)).ToList();
    }

    public async Task RepRestoreAsync(Guid requestId, Guid repUserId, CancellationToken ct = default)
    {
        var rep = await _uow.Repository<SalesRepProfile>().Query()
            .FirstOrDefaultAsync(r => r.UserId == repUserId, ct)
            ?? throw new NotFoundException("Sales rep profile", repUserId);

        var request = await _uow.Repository<QuickRequest>().Query()
            .FirstOrDefaultAsync(r => r.Id == requestId && r.RepId == rep.Id && r.IsDeletedByRep && !r.IsPurgedByRep, ct)
            ?? throw new NotFoundException("Quick request", requestId);

        request.IsDeletedByRep = false;
        request.RepDeletedAt = null;
        await _uow.SaveChangesAsync(ct);
    }

    // ── Coordinator get ───────────────────────────────────────────────────────

    public async Task CoordinatorSoftDeleteAsync(Guid requestId, Guid coordinatorUserId, CancellationToken ct = default)
    {
        var coord = await _uow.Repository<CoordinatorProfile>().Query()
            .FirstOrDefaultAsync(c => c.UserId == coordinatorUserId, ct)
            ?? throw new NotFoundException("Coordinator profile", coordinatorUserId);

        var repIds = await _uow.Repository<RepCoordinator>().Query()
            .Where(rc => rc.CoordinatorId == coord.Id)
            .Select(rc => rc.RepId)
            .ToListAsync(ct);

        var request = await _uow.Repository<QuickRequest>().Query()
            .FirstOrDefaultAsync(r => r.Id == requestId && repIds.Contains(r.RepId) && !r.IsDeletedByCoordinator && !r.IsPurgedByCoordinator, ct)
            ?? throw new NotFoundException("Quick request", requestId);

        request.IsDeletedByCoordinator = true;
        request.CoordinatorDeletedAt = DateTime.UtcNow;
        await _uow.SaveChangesAsync(ct);
    }

    public async Task<List<QuickRequestDto>> GetForCoordinatorAsync(Guid coordinatorUserId, string? type = null, string? status = null, CancellationToken ct = default)
    {
        var coord = await _uow.Repository<CoordinatorProfile>().Query()
            .FirstOrDefaultAsync(c => c.UserId == coordinatorUserId, ct)
            ?? throw new NotFoundException("Coordinator profile", coordinatorUserId);

        // Coordinator sees quick requests from reps assigned to them
        var repIds = await _uow.Repository<RepCoordinator>().Query()
            .Where(rc => rc.CoordinatorId == coord.Id)
            .Select(rc => rc.RepId)
            .ToListAsync(ct);

        var query = _uow.Repository<QuickRequest>().Query()
            .Include(r => r.Images).Include(r => r.Rep)
            .Where(r => repIds.Contains(r.RepId) && !r.IsDeletedByCoordinator && !r.IsPurgedByCoordinator);

        if (!string.IsNullOrEmpty(type) && Enum.TryParse<QuickRequestType>(type, ignoreCase: true, out var t))
            query = query.Where(r => r.Type == t);

        if (!string.IsNullOrEmpty(status) && Enum.TryParse<QuickRequestStatus>(status, ignoreCase: true, out var s))
            query = query.Where(r => r.Status == s);

        var list = await query.OrderByDescending(r => r.CreatedAt).ToListAsync(ct);
        return list.Select(r => MapToDto(r, r.Rep?.FullName ?? string.Empty)).ToList();
    }

    public async Task<QuickRequestDto> CoordinatorUpdateStatusAsync(
        Guid requestId, UpdateQuickRequestStatusDto dto, Guid coordinatorUserId, CancellationToken ct = default)
    {
        var coord = await _uow.Repository<CoordinatorProfile>().Query()
            .FirstOrDefaultAsync(c => c.UserId == coordinatorUserId, ct)
            ?? throw new NotFoundException("Coordinator profile", coordinatorUserId);

        var repIds = await _uow.Repository<RepCoordinator>().Query()
            .Where(rc => rc.CoordinatorId == coord.Id)
            .Select(rc => rc.RepId)
            .ToListAsync(ct);

        var request = await _uow.Repository<QuickRequest>().Query()
            .Include(r => r.Images).Include(r => r.Rep)
            .FirstOrDefaultAsync(r => r.Id == requestId && repIds.Contains(r.RepId), ct)
            ?? throw new NotFoundException("Quick request", requestId);

        if (!Enum.TryParse<QuickRequestStatus>(dto.Status, ignoreCase: true, out var newStatus))
            throw new ArgumentException($"Invalid status '{dto.Status}'.");

        request.Status = newStatus;
        request.UpdatedAt = DateTime.UtcNow;
        await _uow.SaveChangesAsync(ct);

        return MapToDto(request, request.Rep?.FullName ?? string.Empty);
    }

    public async Task<List<QuickRequestDto>> CoordinatorGetTrashAsync(
        Guid coordinatorUserId, string? type = null, CancellationToken ct = default)
    {
        var coord = await _uow.Repository<CoordinatorProfile>().Query()
            .FirstOrDefaultAsync(c => c.UserId == coordinatorUserId, ct)
            ?? throw new NotFoundException("Coordinator profile", coordinatorUserId);

        var repIds = await _uow.Repository<RepCoordinator>().Query()
            .Where(rc => rc.CoordinatorId == coord.Id)
            .Select(rc => rc.RepId)
            .ToListAsync(ct);

        var query = _uow.Repository<QuickRequest>().Query()
            .Include(r => r.Images).Include(r => r.Rep)
            .Where(r => repIds.Contains(r.RepId) && r.IsDeletedByCoordinator && !r.IsPurgedByCoordinator);

        if (!string.IsNullOrEmpty(type) && Enum.TryParse<QuickRequestType>(type, ignoreCase: true, out var t))
            query = query.Where(r => r.Type == t);

        var list = await query.OrderByDescending(r => r.CoordinatorDeletedAt).ToListAsync(ct);
        return list.Select(r => MapToDto(r, r.Rep?.FullName ?? string.Empty)).ToList();
    }

    public async Task CoordinatorRestoreAsync(Guid requestId, Guid coordinatorUserId, CancellationToken ct = default)
    {
        var coord = await _uow.Repository<CoordinatorProfile>().Query()
            .FirstOrDefaultAsync(c => c.UserId == coordinatorUserId, ct)
            ?? throw new NotFoundException("Coordinator profile", coordinatorUserId);

        var repIds = await _uow.Repository<RepCoordinator>().Query()
            .Where(rc => rc.CoordinatorId == coord.Id)
            .Select(rc => rc.RepId)
            .ToListAsync(ct);

        var request = await _uow.Repository<QuickRequest>().Query()
            .FirstOrDefaultAsync(r => r.Id == requestId && repIds.Contains(r.RepId) && r.IsDeletedByCoordinator && !r.IsPurgedByCoordinator, ct)
            ?? throw new NotFoundException("Quick request", requestId);

        request.IsDeletedByCoordinator = false;
        request.CoordinatorDeletedAt = null;
        await _uow.SaveChangesAsync(ct);
    }

    private static DateTime AsUtc(DateTime value) => value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();

    private static DateTime? AsUtc(DateTime? value) => value.HasValue ? AsUtc(value.Value) : null;

    private static void ValidateImage(IFormFile image)
    {
        var extension = Path.GetExtension(image.FileName);
        if (!AllowedImageTypes.Contains(image.ContentType) || !AllowedImageExtensions.Contains(extension))
            throw new BusinessException("Image must be a PNG, JPG, JPEG or WEBP file.", "INVALID_QUICK_REQUEST_IMAGE_TYPE");

        if (image.Length > MaxImageSize)
            throw new BusinessException("Image size cannot exceed 5 MB.", "QUICK_REQUEST_IMAGE_TOO_LARGE");
    }

    private async Task<QuickRequestImage> SaveImageAsync(Guid requestId, IFormFile image, CancellationToken ct)
    {
        var saved = await _fileStorage.SaveAsync(
            image, "quick-requests", FileAccessCategory.Private,
            AllowedImageExtensions, AllowedImageTypes, MaxImageSize, ct);

        return new QuickRequestImage
        {
            QuickRequestId = requestId,
            ImageUrl = saved.StorageKey,
        };
    }

    /// <summary>Resolves the storage key for one image of a request the caller has already been confirmed to have access to. Returns null if the image doesn't belong to that request.</summary>
    public async Task<string?> GetImageStorageKeyAsync(Guid requestId, Guid imageId, CancellationToken ct = default)
    {
        var image = await _uow.Repository<QuickRequestImage>().Query()
            .FirstOrDefaultAsync(i => i.Id == imageId && i.QuickRequestId == requestId, ct);
        return image?.ImageUrl;
    }

    private static QuickRequestDto MapToDto(QuickRequest r, string repName) => new()
    {
        Id = r.Id,
        RequestNumber = r.RequestNumber,
        Type = r.Type.ToString(),
        CustomerName = r.CustomerName,
        Details = r.Details,
        Status = r.Status.ToString(),
        AdminNotes = r.AdminNotes,
        RepId = r.RepId,
        RepName = repName,
        // Exposes the authenticated download endpoint (never a direct file URL) — attachments are private.
        ImageUrls = r.Images.Select(i => $"/api/quick-requests/{r.Id}/images/{i.Id}").ToList(),
        CreatedAt = AsUtc(r.CreatedAt),
        UpdatedAt = AsUtc(r.UpdatedAt),
        DeletedAt = AsUtc(r.AdminDeletedAt ?? r.CoordinatorDeletedAt ?? r.RepDeletedAt),
    };
}
