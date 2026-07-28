using DistributionSystem.Application.DTOs.Region;
using DistributionSystem.Application.Exceptions;
using DistributionSystem.Application.Services.Interfaces;
using DistributionSystem.Domain.Entities;
using DistributionSystem.Infrastructure.Data.Repositories.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace DistributionSystem.Application.Services.Implementations;

public class RegionService : IRegionService
{
    private readonly IUnitOfWork _unitOfWork;

    public RegionService(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<List<RegionDto>> GetAllRegionsAsync(CancellationToken ct = default)
    {
        var regions = await _unitOfWork.Repository<Region>().Query()
            .Include(r => r.SubRegions)
            .Include(r => r.Coordinators)
            .OrderBy(r => r.Name)
            .ToListAsync(ct);

        return regions.Select(MapToDto).ToList();
    }

    public async Task<RegionDto> GetRegionByIdAsync(Guid id, CancellationToken ct = default)
    {
        var region = await _unitOfWork.Repository<Region>().Query()
            .Include(r => r.SubRegions)
            .Include(r => r.Coordinators)
            .FirstOrDefaultAsync(r => r.Id == id, ct)
            ?? throw new NotFoundException("Region", id);

        return MapToDto(region);
    }

    public async Task<RegionDto> CreateRegionAsync(CreateRegionRequest request, CancellationToken ct = default)
    {
        if (await _unitOfWork.Repository<Region>().AnyAsync(r => r.Name == request.Name, ct))
            throw new BusinessException($"Region '{request.Name}' already exists.");

        var region = new Region { Name = request.Name };
        await _unitOfWork.Repository<Region>().AddAsync(region, ct);
        await _unitOfWork.SaveChangesAsync(ct);

        return MapToDto(region);
    }

    public async Task<RegionDto> UpdateRegionAsync(Guid id, UpdateRegionRequest request, CancellationToken ct = default)
    {
        var region = await _unitOfWork.Repository<Region>().Query()
            .Include(r => r.SubRegions)
            .Include(r => r.Coordinators)
            .FirstOrDefaultAsync(r => r.Id == id, ct)
            ?? throw new NotFoundException("Region", id);

        if (request.Name != null)
        {
            if (await _unitOfWork.Repository<Region>().AnyAsync(r => r.Name == request.Name && r.Id != id, ct))
                throw new BusinessException($"Region '{request.Name}' already exists.");
            region.Name = request.Name;
        }
        if (request.IsActive.HasValue)
            region.IsActive = request.IsActive.Value;

        _unitOfWork.Repository<Region>().Update(region);
        await _unitOfWork.SaveChangesAsync(ct);

        return MapToDto(region);
    }

    public async Task DeleteRegionAsync(Guid id, CancellationToken ct = default)
    {
        var region = await _unitOfWork.Repository<Region>().GetByIdAsync(id, ct)
            ?? throw new NotFoundException("Region", id);

        _unitOfWork.Repository<Region>().Remove(region);
        await _unitOfWork.SaveChangesAsync(ct);
    }

    public async Task<List<SubRegionDto>> GetSubRegionsByRegionAsync(Guid regionId, CancellationToken ct = default)
    {
        var subs = await _unitOfWork.Repository<SubRegion>().Query()
            .Include(s => s.Region)
            .Where(s => s.RegionId == regionId)
            .OrderBy(s => s.Name)
            .ToListAsync(ct);

        return subs.Select(MapSubToDto).ToList();
    }

    public async Task<SubRegionDto> CreateSubRegionAsync(CreateSubRegionRequest request, CancellationToken ct = default)
    {
        if (!await _unitOfWork.Repository<Region>().AnyAsync(r => r.Id == request.RegionId, ct))
            throw new NotFoundException("Region", request.RegionId);

        if (await _unitOfWork.Repository<SubRegion>().AnyAsync(
            s => s.RegionId == request.RegionId && s.Name == request.Name, ct))
            throw new BusinessException($"Sub-region '{request.Name}' already exists in this region.");

        var sub = new SubRegion { Name = request.Name, RegionId = request.RegionId };
        await _unitOfWork.Repository<SubRegion>().AddAsync(sub, ct);
        await _unitOfWork.SaveChangesAsync(ct);

        sub.Region = (await _unitOfWork.Repository<Region>().GetByIdAsync(request.RegionId, ct))!;
        return MapSubToDto(sub);
    }

    public async Task<SubRegionDto> UpdateSubRegionAsync(Guid id, UpdateSubRegionRequest request, CancellationToken ct = default)
    {
        var sub = await _unitOfWork.Repository<SubRegion>().Query()
            .Include(s => s.Region)
            .FirstOrDefaultAsync(s => s.Id == id, ct)
            ?? throw new NotFoundException("SubRegion", id);

        if (request.Name != null) sub.Name = request.Name;
        if (request.IsActive.HasValue) sub.IsActive = request.IsActive.Value;

        _unitOfWork.Repository<SubRegion>().Update(sub);
        await _unitOfWork.SaveChangesAsync(ct);

        return MapSubToDto(sub);
    }

    public async Task DeleteSubRegionAsync(Guid id, CancellationToken ct = default)
    {
        var sub = await _unitOfWork.Repository<SubRegion>().GetByIdAsync(id, ct)
            ?? throw new NotFoundException("SubRegion", id);

        _unitOfWork.Repository<SubRegion>().Remove(sub);
        await _unitOfWork.SaveChangesAsync(ct);
    }

    private static RegionDto MapToDto(Region r) => new()
    {
        Id = r.Id,
        Name = r.Name,
        IsActive = r.IsActive,
        CreatedAt = r.CreatedAt,
        SubRegionCount = r.SubRegions?.Count ?? 0,
        CoordinatorCount = r.Coordinators?.Count ?? 0,
        SubRegions = r.SubRegions?.Select(MapSubToDto).ToList() ?? [],
    };

    private static SubRegionDto MapSubToDto(SubRegion s) => new()
    {
        Id = s.Id,
        Name = s.Name,
        RegionId = s.RegionId,
        RegionName = s.Region?.Name,
        IsActive = s.IsActive,
        CreatedAt = s.CreatedAt,
    };
}
