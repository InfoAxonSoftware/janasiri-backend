using DistributionSystem.Application.DTOs.Region;

namespace DistributionSystem.Application.Services.Interfaces;

public interface IRegionService
{
    Task<List<RegionDto>> GetAllRegionsAsync(CancellationToken ct = default);
    Task<RegionDto> GetRegionByIdAsync(Guid id, CancellationToken ct = default);
    Task<RegionDto> CreateRegionAsync(CreateRegionRequest request, CancellationToken ct = default);
    Task<RegionDto> UpdateRegionAsync(Guid id, UpdateRegionRequest request, CancellationToken ct = default);
    Task DeleteRegionAsync(Guid id, CancellationToken ct = default);

    Task<List<SubRegionDto>> GetSubRegionsByRegionAsync(Guid regionId, CancellationToken ct = default);
    Task<SubRegionDto> CreateSubRegionAsync(CreateSubRegionRequest request, CancellationToken ct = default);
    Task<SubRegionDto> UpdateSubRegionAsync(Guid id, UpdateSubRegionRequest request, CancellationToken ct = default);
    Task DeleteSubRegionAsync(Guid id, CancellationToken ct = default);
}
