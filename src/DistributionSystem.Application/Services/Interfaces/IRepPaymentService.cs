using DistributionSystem.Application.DTOs.Common;
using DistributionSystem.Application.DTOs.RepPayments;
using Microsoft.AspNetCore.Http;

namespace DistributionSystem.Application.Services.Interfaces;

public interface IRepPaymentService
{
    // Rep
    Task<RepPaymentDto> CreateAsync(Guid repUserId, CreateRepPaymentDto dto, IFormFile? image, CancellationToken ct = default);
    Task<PagedResult<RepPaymentDto>> GetRepPaymentsAsync(Guid repUserId, RepPaymentQueryDto query, bool trash = false, CancellationToken ct = default);

    // Backward-compatible overload retained for existing callers/tests.
    Task<List<RepPaymentDto>> GetRepPaymentsAsync(Guid repUserId, bool trash = false, CancellationToken ct = default);
    Task<RepPaymentDto> GetForRepByIdAsync(Guid id, Guid repUserId, CancellationToken ct = default);

    /// <summary>Resolves the evidence storage key for a payment the caller has already been confirmed to have access to (via a role-appropriate GetById* method). Returns null if there's no evidence image.</summary>
    Task<string?> GetEvidenceStorageKeyAsync(Guid id, CancellationToken ct = default);
    Task DeleteRepAsync(Guid id, Guid repUserId, CancellationToken ct = default);
    Task RepTrashAsync(Guid id, Guid repUserId, CancellationToken ct = default);
    Task RepRestoreAsync(Guid id, Guid repUserId, CancellationToken ct = default);

    // Admin
    Task<PagedResult<RepPaymentDto>> GetAllAsync(RepPaymentQueryDto query, bool trash = false, CancellationToken ct = default);

    // Backward-compatible overload retained for existing callers/tests.
    Task<List<RepPaymentDto>> GetAllAsync(string? status = null, string? repId = null, CancellationToken ct = default);
    Task<List<RepPaymentRepOptionDto>> GetAdminRepOptionsAsync(CancellationToken ct = default);
    Task<RepPaymentDto> GetByIdForAdminAsync(Guid id, CancellationToken ct = default);
    Task<RepPaymentDto> UpdateStatusAsync(Guid id, UpdateRepPaymentStatusDto dto, Guid updatedByUserId, string updatedBy, CancellationToken ct = default);
    Task AdminSoftDeleteAsync(Guid id, string deletedBy, CancellationToken ct = default);
    Task AdminRestoreAsync(Guid id, CancellationToken ct = default);
    Task<PagedResult<RepPaymentDto>> AdminGetTrashAsync(RepPaymentQueryDto query, CancellationToken ct = default);

    // Backward-compatible overload retained for existing callers/tests.
    Task<List<RepPaymentDto>> AdminGetTrashAsync(CancellationToken ct = default);
    Task AdminHardDeleteAsync(Guid id, CancellationToken ct = default);
    Task BulkSoftDeleteAdminAsync(List<Guid> ids, string deletedBy, CancellationToken ct = default);
    Task BulkUpdateStatusAsync(List<Guid> ids, string status, string updatedBy, CancellationToken ct = default);

    // Coordinator
    Task<PagedResult<RepPaymentDto>> GetForCoordinatorAsync(Guid coordinatorUserId, RepPaymentQueryDto query, bool trash = false, CancellationToken ct = default);

    // Backward-compatible overload retained for existing callers/tests.
    Task<List<RepPaymentDto>> GetForCoordinatorAsync(Guid coordinatorUserId, string? status = null, bool trash = false, CancellationToken ct = default);
    Task<List<RepPaymentRepOptionDto>> GetCoordinatorRepOptionsAsync(Guid coordinatorUserId, CancellationToken ct = default);
    Task<RepPaymentDto> GetForCoordinatorByIdAsync(Guid id, Guid coordinatorUserId, CancellationToken ct = default);
    Task<RepPaymentDto> CoordinatorUpdateStatusAsync(Guid id, UpdateRepPaymentStatusDto dto, Guid coordinatorUserId, CancellationToken ct = default);
    Task CoordinatorTrashAsync(Guid id, Guid coordinatorUserId, CancellationToken ct = default);
    Task CoordinatorRestoreAsync(Guid id, Guid coordinatorUserId, CancellationToken ct = default);
}