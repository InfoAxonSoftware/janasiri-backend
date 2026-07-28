using DistributionSystem.Application.DTOs.Common;
using DistributionSystem.Application.DTOs.Order;

namespace DistributionSystem.Application.Services.Interfaces;

public interface IOrderService
{
    Task<OrderDto> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<PagedResult<OrderDto>> GetAllAsync(OrderFilterRequest filter, CancellationToken cancellationToken = default);
    Task<PagedResult<OrderDto>> GetForCoordinatorAsync(Guid coordinatorUserId, OrderFilterRequest filter, CancellationToken cancellationToken = default);
    Task<PagedResult<UnifiedOrderDto>> GetUnifiedAdminAsync(UnifiedOrderFilterRequest filter, bool trash, CancellationToken cancellationToken = default);
    Task<PagedResult<UnifiedOrderDto>> GetUnifiedRepAsync(Guid repUserId, UnifiedOrderFilterRequest filter, bool trash, CancellationToken cancellationToken = default);
    Task<PagedResult<UnifiedOrderDto>> GetUnifiedCoordinatorAsync(Guid coordinatorUserId, UnifiedOrderFilterRequest filter, bool trash, CancellationToken cancellationToken = default);
    Task<OrderDto> CreateAsync(CreateOrderRequest request, Guid? repId = null, CancellationToken cancellationToken = default);
    Task<OrderDto> ApproveAsync(Guid id, Guid approvedBy, CancellationToken cancellationToken = default);
    Task<OrderDto> RejectAsync(Guid id, string reason, CancellationToken cancellationToken = default);
    Task<OrderDto> UpdateStatusAsync(Guid id, UpdateOrderStatusRequest request, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    // Admin trash (role-specific)
    Task AdminSoftDeleteAsync(Guid id, string deletedBy, CancellationToken cancellationToken = default);
    Task<PagedResult<OrderDto>> AdminGetTrashAsync(int page, int pageSize, CancellationToken cancellationToken = default);
    Task AdminRestoreAsync(Guid id, CancellationToken cancellationToken = default);
    Task AdminRestoreTrashAsync(IReadOnlyCollection<OrderTrashItemRequest> items, CancellationToken cancellationToken = default);
    Task AdminPurgeTrashAsync(IReadOnlyCollection<OrderTrashItemRequest> items, CancellationToken cancellationToken = default);
    Task AdminEmptyTrashAsync(CancellationToken cancellationToken = default);

    // Rep trash
    Task RepSoftDeleteAsync(Guid id, Guid repUserId, CancellationToken cancellationToken = default);
    Task<PagedResult<OrderDto>> RepGetTrashAsync(Guid repUserId, int page, int pageSize, CancellationToken cancellationToken = default);
    Task RepRestoreAsync(Guid id, Guid repUserId, CancellationToken cancellationToken = default);
    Task RepRestoreTrashAsync(Guid repUserId, IReadOnlyCollection<OrderTrashItemRequest> items, CancellationToken cancellationToken = default);
    Task RepPurgeTrashAsync(Guid repUserId, IReadOnlyCollection<OrderTrashItemRequest> items, CancellationToken cancellationToken = default);
    Task RepEmptyTrashAsync(Guid repUserId, CancellationToken cancellationToken = default);

    // Coordinator trash
    Task CoordinatorSoftDeleteAsync(Guid id, Guid coordinatorUserId, CancellationToken cancellationToken = default);
    Task<PagedResult<OrderDto>> CoordinatorGetTrashAsync(Guid coordinatorUserId, int page, int pageSize, CancellationToken cancellationToken = default);
    Task CoordinatorRestoreAsync(Guid id, Guid coordinatorUserId, CancellationToken cancellationToken = default);
    Task CoordinatorRestoreTrashAsync(Guid coordinatorUserId, IReadOnlyCollection<OrderTrashItemRequest> items, CancellationToken cancellationToken = default);
    Task CoordinatorPurgeTrashAsync(Guid coordinatorUserId, IReadOnlyCollection<OrderTrashItemRequest> items, CancellationToken cancellationToken = default);
    Task CoordinatorEmptyTrashAsync(Guid coordinatorUserId, CancellationToken cancellationToken = default);

    // Customer trash
    Task CustomerSoftDeleteAsync(Guid id, Guid customerUserId, CancellationToken cancellationToken = default);
    Task<PagedResult<OrderDto>> CustomerGetTrashAsync(Guid customerUserId, int page, int pageSize, CancellationToken cancellationToken = default);
    Task CustomerRestoreAsync(Guid id, Guid customerUserId, CancellationToken cancellationToken = default);
    Task<OrderDto> CancelAsync(Guid id, string reason, CancellationToken cancellationToken = default);
    Task<OrderDto> ConfirmDeliveryAsync(Guid id, CancellationToken cancellationToken = default);
    Task<OrderDto> RateOrderAsync(Guid id, RateOrderRequest request, CancellationToken cancellationToken = default);
    Task<PagedResult<OrderDto>> GetCustomerOrdersAsync(Guid customerId, int page, int pageSize, CancellationToken cancellationToken = default);
    Task<OrderDto> ReorderAsync(Guid orderId, Guid customerId, CancellationToken cancellationToken = default);
}
