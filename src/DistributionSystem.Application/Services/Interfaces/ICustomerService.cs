using DistributionSystem.Application.DTOs.Common;
using DistributionSystem.Application.DTOs.Customer;
using Microsoft.AspNetCore.Http;

namespace DistributionSystem.Application.Services.Interfaces;

public interface ICustomerService
{
    Task<CustomerDto> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<CustomerDto> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<PagedResult<CustomerDto>> GetAllAsync(int page, int pageSize, string? search = null, string? sortBy = null, string? sortOrder = "asc",
        bool? isActive = null, Guid? assignedRepId = null, Guid? assignedCoordinatorId = null, Guid? regionId = null, Guid? subRegionId = null, string? customerSegment = null,
        decimal? minCreditLimit = null, decimal? maxCreditLimit = null,
        DateTime? createdFrom = null, DateTime? createdTo = null, CancellationToken cancellationToken = default);
    Task<CustomerFilterOptionsDto> GetFilterOptionsAsync(CancellationToken cancellationToken = default);
    Task<PagedResult<CustomerDto>> GetByRepAsync(Guid repUserId, int page, int pageSize, string? search = null, CancellationToken cancellationToken = default);
    Task<CustomerDto> CreateAsync(CreateCustomerRequest request, CancellationToken cancellationToken = default);
    Task<CustomerDto> UpdateAsync(Guid id, UpdateCustomerRequest request, CancellationToken cancellationToken = default);
    Task<CustomerDto> UpdateByUserIdAsync(Guid userId, UpdateCustomerRequest request, CancellationToken cancellationToken = default);
    Task<CustomerSummaryDto> GetSummaryAsync(Guid id, string baseUrl, CancellationToken cancellationToken = default);
    Task<CustomerSummaryDto> UpdateRegistrationDetailsAsync(
        Guid id,
        UpdateCustomerRegistrationDetailsRequest request,
        IFormFile? businessRegDoc,
        IFormFile? businessAddressDoc,
        IFormFile? vatDoc,
        string baseUrl,
        CancellationToken cancellationToken = default);
    Task ActivateAsync(Guid id, CancellationToken cancellationToken = default);
    Task DeactivateAsync(Guid id, CancellationToken cancellationToken = default);
    Task SoftDeleteAsync(Guid id, string deletedBy, CancellationToken cancellationToken = default);
    Task RestoreAsync(Guid id, CancellationToken cancellationToken = default);
    Task<PagedResult<CustomerDto>> GetTrashedAsync(int page, int pageSize, CancellationToken cancellationToken = default);

    Task<List<PriceDetailDto>> GetSpecialPricesAsync(Guid customerId, CancellationToken cancellationToken = default);
    Task SaveSpecialPricesAsync(Guid customerId, IEnumerable<SpecialPriceUpdateRequest> prices, CancellationToken cancellationToken = default);
}
