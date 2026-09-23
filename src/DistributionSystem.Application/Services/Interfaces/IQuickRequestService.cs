using DistributionSystem.Application.DTOs.QuickRequests;
using Microsoft.AspNetCore.Http;

namespace DistributionSystem.Application.Services.Interfaces;

public interface IQuickRequestService
{
    // Rep
    Task<QuickRequestDto> CreateAsync(Guid repUserId, CreateQuickRequestDto dto, CancellationToken ct = default);
    Task<QuickRequestDto> AddImagesAsync(Guid requestId, Guid repUserId, IList<IFormFile> images, CancellationToken ct = default);
    Task<List<QuickRequestDto>> GetRepRequestsAsync(Guid repUserId, string? type = null, CancellationToken ct = default);
    Task<QuickRequestDto> GetRepRequestByIdAsync(Guid requestId, Guid repUserId, CancellationToken ct = default);

    /// <summary>Resolves the storage key for one image of a request the caller has already been confirmed to have access to. Returns null if the image doesn't belong to that request.</summary>
    Task<string?> GetImageStorageKeyAsync(Guid requestId, Guid imageId, CancellationToken ct = default);

    // Rep trash
    Task RepSoftDeleteAsync(Guid requestId, Guid repUserId, CancellationToken ct = default);
    Task<List<QuickRequestDto>> RepGetTrashAsync(Guid repUserId, string? type = null, CancellationToken ct = default);
    Task RepRestoreAsync(Guid requestId, Guid repUserId, CancellationToken ct = default);

    // Coordinator
    Task<List<QuickRequestDto>> GetForCoordinatorAsync(Guid coordinatorUserId, string? type = null, string? status = null, CancellationToken ct = default);
    Task CoordinatorSoftDeleteAsync(Guid requestId, Guid coordinatorUserId, CancellationToken ct = default);
    Task<QuickRequestDto> CoordinatorUpdateStatusAsync(Guid requestId, UpdateQuickRequestStatusDto dto, Guid coordinatorUserId, CancellationToken ct = default);
    Task<List<QuickRequestDto>> CoordinatorGetTrashAsync(Guid coordinatorUserId, string? type = null, CancellationToken ct = default);
    Task CoordinatorRestoreAsync(Guid requestId, Guid coordinatorUserId, CancellationToken ct = default);

    // Admin
    Task<List<QuickRequestDto>> GetAllAsync(string? type = null, string? status = null, CancellationToken ct = default);
    Task<QuickRequestDto> GetByIdAsync(Guid requestId, CancellationToken ct = default);
    Task<QuickRequestDto> CreateAdminAsync(Guid adminUserId,string createdBy,CreateQuickRequestDto dto,CancellationToken ct = default);
    Task<QuickRequestDto> AddAdminImagesAsync(Guid requestId,IList<IFormFile> images,CancellationToken ct = default);

    /// <summary>Coordinator-scoped variant of <see cref="GetByIdAsync"/> — throws NotFoundException if the request's rep isn't assigned to this coordinator.</summary>
    Task<QuickRequestDto> GetForCoordinatorByIdAsync(Guid requestId, Guid coordinatorUserId, CancellationToken ct = default);
    Task<QuickRequestDto> UpdateStatusAsync(Guid requestId, UpdateQuickRequestStatusDto dto, string updatedBy, CancellationToken ct = default);
    Task AdminSoftDeleteAsync(Guid requestId, string deletedBy, CancellationToken ct = default);
    Task<List<QuickRequestDto>> AdminGetTrashAsync(string? type = null, CancellationToken ct = default);
    Task AdminRestoreAsync(Guid requestId, CancellationToken ct = default);
    Task DeleteAsync(Guid requestId, CancellationToken ct = default);
}
