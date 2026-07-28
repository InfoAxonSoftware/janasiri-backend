using DistributionSystem.Application.DTOs.Common;
using DistributionSystem.Application.DTOs.Customer;
using Microsoft.AspNetCore.Http;

namespace DistributionSystem.Application.Services.Interfaces;

public interface ICustomerRegistrationService
{
    Task<CustomerRegistrationRequestDto> SubmitAsync(
        SubmitRegistrationFormRequest data,
        IFormFile? businessRegDoc,
        IFormFile? businessAddressDoc,
        IFormFile? vatDoc,
        CancellationToken ct = default);

    Task<PagedResult<CustomerRegistrationRequestDto>> GetAllAsync(
        int page, int pageSize, string? status, string baseUrl, CancellationToken ct = default);

    Task<PagedResult<CustomerRegistrationRequestDto>> GetForCoordinatorAsync(
        Guid coordinatorUserId, int page, int pageSize, string? status, string baseUrl, CancellationToken ct = default);

    Task<CustomerRegistrationRequestDto> GetByIdAsync(
        Guid id, string baseUrl, CancellationToken ct = default);

    /// <summary>Coordinator-scoped variant — throws NotFoundException if the request is outside the coordinator's assigned region.</summary>
    Task<CustomerRegistrationRequestDto> GetByIdForCoordinatorAsync(
        Guid id, Guid coordinatorUserId, string baseUrl, CancellationToken ct = default);

    /// <summary>Resolves the storage key + safe download name for one of the request's KYC documents, or null if that document was never uploaded.</summary>
    Task<(string StorageKey, string DownloadName)?> GetDocumentReferenceAsync(
        Guid id, string docType, CancellationToken ct = default);

    Task<CustomerRegistrationRequestDto> ReviewAsync(
        Guid id, ReviewRegistrationRequest request, Guid adminId, CancellationToken ct = default);

    Task<CustomerRegistrationRequestDto> ReviewByCoordinatorAsync(
        Guid id, ReviewRegistrationRequest request, Guid coordinatorUserId, string baseUrl, CancellationToken ct = default);

    Task<List<CoordinatorOptionDto>> GetCoordinatorOptionsAsync(CancellationToken ct = default);

    Task<CustomerRegistrationRequestDto> AdminCreateAsync(
        AdminCreateRegistrationRequest data,
        IFormFile? businessRegDoc,
        IFormFile? businessAddressDoc,
        IFormFile? vatDoc,
        Guid adminId,
        CancellationToken ct = default);
}
