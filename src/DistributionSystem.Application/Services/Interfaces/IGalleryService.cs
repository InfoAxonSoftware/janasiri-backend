using DistributionSystem.Application.DTOs.Gallery;

namespace DistributionSystem.Application.Services.Interfaces;

public interface IGalleryService
{
    Task<List<GalleryItemDto>> GetAllAsync(bool activeOnly = false, CancellationToken ct = default);
    Task<GalleryItemDto> CreateAsync(string title, string? description, string imageUrl, int displayOrder, string createdBy, CancellationToken ct = default);
    Task<GalleryItemDto> UpdateAsync(Guid id, string title, string? description, int displayOrder, bool isActive, string updatedBy, CancellationToken ct = default);
    Task<GalleryItemDto> AddExtraImageAsync(Guid id, string imageUrl, string updatedBy, CancellationToken ct = default);
    Task<GalleryItemDto> RemoveExtraImageAsync(Guid id, int index, string updatedBy, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}
