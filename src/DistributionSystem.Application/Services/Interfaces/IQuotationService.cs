using DistributionSystem.Application.DTOs.Common;
using DistributionSystem.Application.DTOs.Quotation;

namespace DistributionSystem.Application.Services.Interfaces;

public interface IQuotationService
{
    // Customer endpoints
    Task<QuotationDto> CustomerCreateQuotationAsync(Guid userId, CreateQuotationRequest request, CancellationToken ct);
    Task<PagedResult<QuotationDto>> CustomerGetQuotationsAsync(Guid userId, int page, int pageSize, string? status, CancellationToken ct);
    Task<QuotationDto> CustomerGetQuotationByIdAsync(Guid userId, Guid quotationId, CancellationToken ct);
    Task<Guid> ConvertQuotationToOrderAsync(Guid userId, Guid quotationId, ConvertQuotationToOrderRequest request, CancellationToken ct);
    Task<QuotationDto> CustomerCancelQuotationAsync(Guid userId, Guid quotationId, CancelQuotationRequest request, CancellationToken ct);

    // Rep endpoints
    Task<QuotationDto> RepCreateQuotationAsync(Guid userId, CreateQuotationRequest request, CancellationToken ct);
    Task<PagedResult<QuotationDto>> RepGetQuotationsAsync(Guid userId, int page, int pageSize, string? status, CancellationToken ct);
    Task<QuotationDto> RepGetQuotationByIdAsync(Guid userId, Guid quotationId, CancellationToken ct);

    // Coordinator endpoints
    Task<PagedResult<QuotationDto>> CoordinatorGetQuotationsAsync(Guid userId, int page, int pageSize, string? status, CancellationToken ct);
    Task<QuotationDto> CoordinatorGetQuotationByIdAsync(Guid userId, Guid quotationId, CancellationToken ct);
    Task<QuotationDto> ApproveQuotationAsync(Guid userId, Guid quotationId, ApproveQuotationRequest request, CancellationToken ct);
    Task RejectQuotationAsync(Guid userId, Guid quotationId, RejectQuotationRequest request, CancellationToken ct);

    // Admin endpoints
    Task<PagedResult<QuotationDto>> AdminGetAllQuotationsAsync(int page, int pageSize, string? status, string? search, CancellationToken ct);
    Task<QuotationDto> AdminGetQuotationByIdAsync(Guid quotationId, CancellationToken ct);
    Task<QuotationDto> AdminApproveQuotationAsync(Guid userId, Guid quotationId, ApproveQuotationRequest request, CancellationToken ct);
    Task AdminRejectQuotationAsync(Guid userId, Guid quotationId, RejectQuotationRequest request, CancellationToken ct);
    Task AdminSoftDeleteAsync(Guid quotationId, string deletedBy, CancellationToken ct);
    Task<PagedResult<QuotationDto>> AdminGetTrashAsync(int page, int pageSize, CancellationToken ct);
    Task AdminRestoreAsync(Guid quotationId, CancellationToken ct);

    // Rep trash
    Task RepSoftDeleteAsync(Guid quotationId, Guid repUserId, CancellationToken ct);
    Task<PagedResult<QuotationDto>> RepGetTrashAsync(Guid repUserId, int page, int pageSize, CancellationToken ct);
    Task RepRestoreAsync(Guid quotationId, Guid repUserId, CancellationToken ct);

    // Coordinator trash
    Task CoordinatorSoftDeleteAsync(Guid quotationId, Guid coordinatorUserId, CancellationToken ct);
    Task<PagedResult<QuotationDto>> CoordinatorGetTrashAsync(Guid coordinatorUserId, int page, int pageSize, CancellationToken ct);
    Task CoordinatorRestoreAsync(Guid quotationId, Guid coordinatorUserId, CancellationToken ct);

    // Customer trash
    Task CustomerSoftDeleteAsync(Guid quotationId, Guid customerUserId, CancellationToken ct);
    Task<PagedResult<QuotationDto>> CustomerGetTrashAsync(Guid customerUserId, int page, int pageSize, CancellationToken ct);
    Task CustomerRestoreAsync(Guid quotationId, Guid customerUserId, CancellationToken ct);
}
