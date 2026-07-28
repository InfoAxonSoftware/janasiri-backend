using DistributionSystem.Application.DTOs.Common;
using DistributionSystem.Application.DTOs.Rep;

namespace DistributionSystem.Application.Services.Interfaces;

public interface IRepService
{
    Task<RepDto> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<RepDto> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<RepDto> UpdateByUserIdAsync(Guid userId, UpdateRepRequest request, CancellationToken cancellationToken = default);
    Task<PagedResult<RepDto>> GetAllAsync(int page, int pageSize, string? search = null, Guid? coordinatorId = null, Guid? regionId = null, bool? isActive = null, CancellationToken cancellationToken = default);
    Task<RepDto> CreateAsync(CreateRepRequest request, CancellationToken cancellationToken = default);
    Task<RepDto> UpdateAsync(Guid id, UpdateRepRequest request, CancellationToken cancellationToken = default);
    Task UnassignCoordinatorAsync(Guid id, CancellationToken cancellationToken = default);
    Task<RepPerformanceDto> GetPerformanceAsync(Guid repId, DateTime from, DateTime to, CancellationToken cancellationToken = default);
    Task<RepPerformanceDto> GetPerformanceByUserIdAsync(Guid userId, DateTime from, DateTime to, CancellationToken cancellationToken = default);
    Task<List<VisitDto>> GetTodayVisitsAsync(Guid repUserId, CancellationToken cancellationToken = default);
    Task<VisitDto> AddAdHocVisitAsync(Guid repUserId, Guid customerId, string? notes = null, CancellationToken cancellationToken = default);

    // get a single visit (for rep detail page)
    Task<VisitDto> GetVisitByIdAsync(Guid repUserId, Guid visitId, CancellationToken cancellationToken = default);

    // helper used by background job and for manual testing
    Task GenerateTodayVisitsAsync(CancellationToken cancellationToken = default);

    // return details of a specific route assigned to the rep
    Task<RouteDto> GetRouteDetailsAsync(Guid repUserId, Guid routeId, CancellationToken cancellationToken = default);
    Task<VisitDto> CheckInAsync(Guid repUserId, CheckInRequest request, CancellationToken cancellationToken = default);
    Task<VisitDto> CheckOutAsync(Guid visitId, CheckOutRequest request, CancellationToken cancellationToken = default);
    // Routes
    Task<RouteDto> CreateRouteAsync(CreateRouteRequest request, CancellationToken cancellationToken = default);
    Task AssignRouteAsync(Guid routeId, AssignRouteRequest request, CancellationToken cancellationToken = default);
    Task CancelVisitAsync(Guid repUserId, Guid visitId, CancellationToken cancellationToken = default);
    Task AddCustomerToRouteAsync(Guid routeId, AddRouteCustomerRequest request, CancellationToken cancellationToken = default);
    Task RemoveCustomerFromRouteAsync(Guid routeId, Guid customerId, CancellationToken cancellationToken = default);
    Task RemoveRepFromRouteAsync(Guid routeId, Guid repId, CancellationToken cancellationToken = default);
    Task<List<RouteDto>> GetAllRoutesAsync(CancellationToken cancellationToken = default);
    Task<List<RouteDto>> GetRepRoutesAsync(Guid repUserId, CancellationToken cancellationToken = default);
    Task DeleteRouteAsync(Guid routeId, CancellationToken cancellationToken = default);
    // Admin progress
    Task<List<RouteProgressDto>> GetTodayRouteProgressAsync(CancellationToken cancellationToken = default);

    // Targets
    Task SetTargetAsync(Guid repId, SalesTargetDto target, CancellationToken cancellationToken = default);
    Task<List<SalesTargetDto>> GetTargetsAsync(Guid repUserId, CancellationToken cancellationToken = default);
    Task<List<SalesTargetDto>> GetTargetsByRepIdAsync(Guid repId, CancellationToken cancellationToken = default);
    Task DeleteTargetAsync(Guid targetId, CancellationToken cancellationToken = default);
    Task<List<LeaderboardDto>> GetLeaderboardAsync(DateTime from, DateTime to, CancellationToken cancellationToken = default);
    // Trash / soft-delete
    Task SoftDeleteAsync(Guid id, string deletedBy, CancellationToken cancellationToken = default);
    Task RestoreAsync(Guid id, CancellationToken cancellationToken = default);
    Task<PagedResult<RepDto>> GetTrashedAsync(int page, int pageSize, CancellationToken cancellationToken = default);
    Task PurgeOldTrashedAsync(int olderThanDays, CancellationToken cancellationToken = default);
}
