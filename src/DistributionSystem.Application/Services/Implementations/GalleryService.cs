using System.Text.Json;
using DistributionSystem.Application.DTOs.Gallery;
using DistributionSystem.Application.Services.Interfaces;
using DistributionSystem.Domain.Entities;
using DistributionSystem.Infrastructure.Data.Repositories.Interfaces;

namespace DistributionSystem.Application.Services.Implementations;

public class GalleryService : IGalleryService
{
    private readonly IUnitOfWork _unitOfWork;

    public GalleryService(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<List<GalleryItemDto>> GetAllAsync(bool activeOnly = false, CancellationToken ct = default)
    {
        var query = _unitOfWork.Repository<GalleryItem>().Query();
        if (activeOnly)
            query = query.Where(x => x.IsActive);

        return query
            .OrderBy(x => x.DisplayOrder)
            .ThenByDescending(x => x.CreatedAt)
            .AsEnumerable()
            .Select(x => MapToDto(x))
            .ToList();
    }

    public async Task<GalleryItemDto> CreateAsync(string title, string? description, string imageUrl, int displayOrder, string createdBy, CancellationToken ct = default)
    {
        var item = new GalleryItem
        {
            Title = title,
            Description = description,
            ImageUrl = imageUrl,
            DisplayOrder = displayOrder,
            IsActive = true,
            CreatedBy = createdBy,
            CreatedAt = DateTime.UtcNow
        };
        await _unitOfWork.Repository<GalleryItem>().AddAsync(item, ct);
        await _unitOfWork.SaveChangesAsync(ct);
        return MapToDto(item);
    }

    public async Task<GalleryItemDto> UpdateAsync(Guid id, string title, string? description, int displayOrder, bool isActive, string updatedBy, CancellationToken ct = default)
    {
        var item = await _unitOfWork.Repository<GalleryItem>().GetByIdAsync(id, ct)
            ?? throw new KeyNotFoundException($"Gallery item {id} not found");

        item.Title = title;
        item.Description = description;
        item.DisplayOrder = displayOrder;
        item.IsActive = isActive;
        item.UpdatedBy = updatedBy;
        item.UpdatedAt = DateTime.UtcNow;

        _unitOfWork.Repository<GalleryItem>().Update(item);
        await _unitOfWork.SaveChangesAsync(ct);
        return MapToDto(item);
    }

    public async Task<GalleryItemDto> AddExtraImageAsync(Guid id, string imageUrl, string updatedBy, CancellationToken ct = default)
    {
        var item = await _unitOfWork.Repository<GalleryItem>().GetByIdAsync(id, ct)
            ?? throw new KeyNotFoundException($"Gallery item {id} not found");

        var extras = DeserializeExtras(item.ExtraImageUrlsJson);
        extras.Add(imageUrl);
        item.ExtraImageUrlsJson = JsonSerializer.Serialize(extras);
        item.UpdatedBy = updatedBy;
        item.UpdatedAt = DateTime.UtcNow;

        _unitOfWork.Repository<GalleryItem>().Update(item);
        await _unitOfWork.SaveChangesAsync(ct);
        return MapToDto(item);
    }

    public async Task<GalleryItemDto> RemoveExtraImageAsync(Guid id, int index, string updatedBy, CancellationToken ct = default)
    {
        var item = await _unitOfWork.Repository<GalleryItem>().GetByIdAsync(id, ct)
            ?? throw new KeyNotFoundException($"Gallery item {id} not found");

        var extras = DeserializeExtras(item.ExtraImageUrlsJson);
        if (index >= 0 && index < extras.Count)
            extras.RemoveAt(index);

        item.ExtraImageUrlsJson = extras.Count > 0 ? JsonSerializer.Serialize(extras) : null;
        item.UpdatedBy = updatedBy;
        item.UpdatedAt = DateTime.UtcNow;

        _unitOfWork.Repository<GalleryItem>().Update(item);
        await _unitOfWork.SaveChangesAsync(ct);
        return MapToDto(item);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var item = await _unitOfWork.Repository<GalleryItem>().GetByIdAsync(id, ct)
            ?? throw new KeyNotFoundException($"Gallery item {id} not found");

        _unitOfWork.Repository<GalleryItem>().Remove(item);
        await _unitOfWork.SaveChangesAsync(ct);
    }

    private static List<string> DeserializeExtras(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new List<string>();
        try { return JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>(); }
        catch { return new List<string>(); }
    }

    private static GalleryItemDto MapToDto(GalleryItem x) => new()
    {
        Id = x.Id,
        Title = x.Title,
        Description = x.Description,
        ImageUrl = x.ImageUrl,
        ExtraImageUrls = DeserializeExtras(x.ExtraImageUrlsJson),
        DisplayOrder = x.DisplayOrder,
        IsActive = x.IsActive,
        CreatedAt = x.CreatedAt,
        UpdatedAt = x.UpdatedAt
    };
}
