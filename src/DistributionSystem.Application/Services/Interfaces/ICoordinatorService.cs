using DistributionSystem.Application.DTOs.Common;
using DistributionSystem.Application.DTOs.Coordinator;
using DistributionSystem.Application.DTOs.Customer;
using DistributionSystem.Application.DTOs.Quotation;
using DistributionSystem.Application.DTOs.Rep;

namespace DistributionSystem.Application.Services.Interfaces;

public interface ICoordinatorService
{
    // Coordinator CRUD (Admin)
    Task<PagedResult<CoordinatorDto>> GetAllCoordinatorsAsync(int page, int pageSize, string? search, CancellationToken ct);
    Task<CoordinatorDto> GetCoordinatorByIdAsync(Guid id, CancellationToken ct);
    Task<CoordinatorDto> CreateCoordinatorAsync(CreateCoordinatorRequest request, CancellationToken ct);
    Task<CoordinatorDto> UpdateCoordinatorAsync(Guid id, UpdateCoordinatorRequest request, CancellationToken ct);
    Task DeleteCoordinatorAsync(Guid id, CancellationToken ct);
    Task AssignRepToCoordinatorAsync(Guid coordinatorId, Guid repId, CancellationToken ct);

    // Coordinator's own profile
    Task<CoordinatorDto> GetMyProfileAsync(Guid userId, CancellationToken ct);
    Task<CoordinatorDto> UpdateMyProfileAsync(Guid userId, UpdateCoordinatorRequest request, CancellationToken ct);
    Task<CoordinatorDashboardDto> GetDashboardAsync(Guid userId, CancellationToken ct);

    // Customer Approval Workflow
    Task<PagedResult<CustomerDto>> GetPendingCustomerApprovalsAsync(Guid userId, int page, int pageSize, CancellationToken ct);
    Task<CustomerDto> ApproveCustomerAsync(Guid userId, Guid customerId, ApproveCustomerRequest request, CancellationToken ct);
    Task RejectCustomerAsync(Guid userId, Guid customerId, RejectCustomerRequest request, CancellationToken ct);

    // Region & Team
    Task<List<RepDto>> GetAssignedRepsAsync(Guid userId, CancellationToken ct);
    Task<RepDto> GetAssignedRepByIdAsync(Guid userId, Guid repId, CancellationToken ct);
    Task<RepPerformanceDto> GetRepPerformanceAsync(Guid userId, Guid repId, DateTime from, DateTime to, CancellationToken ct);
    Task<PagedResult<CustomerDto>> GetRepCustomersAsync(Guid userId, Guid repId, int page, int pageSize, string? search, CancellationToken ct);
    Task<List<RouteDto>> GetRoutesAsync(Guid userId, CancellationToken ct);
    Task<List<RouteDto>> GetRepRoutesAsync(Guid userId, Guid repId, CancellationToken ct);
    Task<RouteDto> CreateRouteAsync(Guid userId, CreateRouteRequest request, CancellationToken ct);
    Task<RouteDto> CreateRouteForRepAsync(Guid userId, Guid repId, CreateRouteRequest request, CancellationToken ct);
    Task AssignRouteAsync(Guid userId, Guid routeId, Guid repId, CancellationToken ct);
    Task AddCustomerToRouteAsync(Guid userId, Guid routeId, AddRouteCustomerRequest request, CancellationToken ct);
    Task RemoveCustomerFromRouteAsync(Guid userId, Guid routeId, Guid customerId, CancellationToken ct);
    Task DeleteRouteAsync(Guid userId, Guid routeId, CancellationToken ct);
    Task<PagedResult<CustomerDto>> GetRegionCustomersAsync(Guid userId, int page, int pageSize, string? search, CancellationToken ct);
    // Trash / soft-delete
    Task SoftDeleteAsync(Guid id, string deletedBy, CancellationToken ct);
    Task RestoreAsync(Guid id, CancellationToken ct);
    Task<PagedResult<CoordinatorDto>> GetTrashedAsync(int page, int pageSize, CancellationToken ct);
    Task PurgeOldTrashedAsync(int olderThanDays, CancellationToken ct);
}
