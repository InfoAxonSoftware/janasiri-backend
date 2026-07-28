using DistributionSystem.Application.DTOs.Common;
using DistributionSystem.Application.DTOs.Product;

namespace DistributionSystem.Application.Services.Interfaces;

public interface IProductService
{
    Task<ProductDto> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<PagedResult<ProductDto>> GetAllAsync(int page, int pageSize, ProductFilterRequest? filter = null, CancellationToken cancellationToken = default);
    Task<ProductDto> CreateAsync(CreateProductRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Remove all existing products from the system. DEPRECATED — use ImportAsync instead.
    /// </summary>
    Task ClearAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Upsert-based import: match by SKU, update existing products, create new ones.
    /// Existing products NOT in the import are preserved so order history is intact.
    /// </summary>
    /// <summary>
    /// Bulk upsert import. Each request may optionally specify <see cref="CreateProductRequest.MainCategory" />
    /// and <see cref="CreateProductRequest.SubCategory" />.  When names are provided the system will
    /// automatically create the main and/or sub category hierarchy and assign the resulting subcategory
    /// to the product.  Existing products are matched by SKU and updated.
    /// </summary>
    Task<List<ProductDto>> ImportAsync(List<CreateProductRequest> requests, CancellationToken cancellationToken = default);
    Task<ProductDto> UpdateAsync(Guid id, UpdateProductRequest request, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
    Task<int> DeleteManyAsync(List<Guid> ids, CancellationToken cancellationToken = default);
    Task<int> CleanupOrphanCategoriesAsync(CancellationToken cancellationToken = default);
    Task<List<ProductDto>> ReplaceAllAsync(List<CreateProductRequest> requests, CancellationToken cancellationToken = default);
    Task<ProductDto> UpdatePriceAsync(Guid id, UpdatePriceRequest request, CancellationToken cancellationToken = default);
    // Images
    Task<ProductDto> AddImageAsync(Guid id, string imageUrl, CancellationToken cancellationToken = default);
    Task<ProductDto> RemoveImageAsync(Guid id, int index, CancellationToken cancellationToken = default);
    // Category
    Task<List<CategoryDto>> GetCategoriesAsync(CancellationToken cancellationToken = default);
    Task<CategoryDto> CreateCategoryAsync(CreateCategoryRequest request, CancellationToken cancellationToken = default);
}
