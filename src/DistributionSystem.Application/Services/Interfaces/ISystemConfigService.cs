using DistributionSystem.Application.DTOs.Config;

namespace DistributionSystem.Application.Services.Interfaces;

public interface ISystemConfigService
{
    Task<SystemConfigDto> GetConfigAsync(CancellationToken ct);
    Task<SystemConfigDto> UpdateConfigAsync(UpdateSystemConfigRequest request, CancellationToken ct);
}
