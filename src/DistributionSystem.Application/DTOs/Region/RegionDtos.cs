namespace DistributionSystem.Application.DTOs.Region;

public class RegionDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
    public int SubRegionCount { get; set; }
    public int CoordinatorCount { get; set; }
    public List<SubRegionDto> SubRegions { get; set; } = [];
}

public class SubRegionDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public Guid RegionId { get; set; }
    public string? RegionName { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class CreateRegionRequest
{
    public string Name { get; set; } = string.Empty;
}

public class UpdateRegionRequest
{
    public string? Name { get; set; }
    public bool? IsActive { get; set; }
}

public class CreateSubRegionRequest
{
    public string Name { get; set; } = string.Empty;
    public Guid RegionId { get; set; }
}

public class UpdateSubRegionRequest
{
    public string? Name { get; set; }
    public bool? IsActive { get; set; }
}
