using System.Text.Json;
using DistributionSystem.Application.DTOs.Common;
using DistributionSystem.Application.DTOs.Product;
using DistributionSystem.Application.Exceptions;
using DistributionSystem.Application.Services.Interfaces;
using DistributionSystem.Domain.Entities;
using DistributionSystem.Infrastructure.Data.Repositories.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace DistributionSystem.Application.Services.Implementations;

public class ProductService : IProductService
{
    private static readonly SemaphoreSlim ImportGate = new(1, 1);
    private readonly IUnitOfWork _unitOfWork;

    public ProductService(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<ProductDto> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var product = await _unitOfWork.Repository<Product>().Query()
            .Include(p => p.Category)
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken)
            ?? throw new NotFoundException("Product", id);

        return MapToDto(product);
    }

    public async Task<PagedResult<ProductDto>> GetAllAsync(int page, int pageSize, ProductFilterRequest? filter = null, CancellationToken cancellationToken = default)
    {
        filter ??= new ProductFilterRequest();

        var query = _unitOfWork.Repository<Product>().Query()
            .Include(p => p.Category)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var searchTerm = $"%{filter.Search.Trim()}%";
            query = query.Where(p =>
                EF.Functions.ILike(p.Name, searchTerm) ||
                EF.Functions.ILike(p.SKU, searchTerm) ||
                EF.Functions.ILike(p.Brand, searchTerm));
        }

        if (filter.CategoryId.HasValue)
        {
            var cid = filter.CategoryId.Value;
            query = query.Where(p => p.CategoryId == cid || (p.Category != null && p.Category.ParentCategoryId == cid));
        }

        if (!string.IsNullOrWhiteSpace(filter.Brand))
        {
            var brandTerm = $"%{filter.Brand.Trim()}%";
            query = query.Where(p => EF.Functions.ILike(p.Brand, brandTerm));
        }

        if (filter.MinPrice.HasValue)
            query = query.Where(p => p.SellingPrice >= filter.MinPrice.Value);

        if (filter.MaxPrice.HasValue)
            query = query.Where(p => p.SellingPrice <= filter.MaxPrice.Value);



        // sorting
        switch (filter.SortBy?.ToLowerInvariant())
        {
            case "price":
                query = filter.SortDir?.ToLowerInvariant() == "desc" ? query.OrderByDescending(p => p.SellingPrice) : query.OrderBy(p => p.SellingPrice);
                break;
            case "createdat":
                query = filter.SortDir?.ToLowerInvariant() == "desc" ? query.OrderByDescending(p => p.CreatedAt) : query.OrderBy(p => p.CreatedAt);
                break;
            default:
                query = filter.SortDir?.ToLowerInvariant() == "desc" ? query.OrderByDescending(p => p.Name) : query.OrderBy(p => p.Name);
                break;
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<ProductDto>
        {
            Items = items.Select(MapToDto),
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize
        };
    }

    public async Task<ProductDto> CreateAsync(CreateProductRequest request, CancellationToken cancellationToken = default)
    {
        if (await _unitOfWork.Repository<Product>().AnyAsync(p => p.SKU == request.SKU, cancellationToken))
            throw new BusinessException("Product with this SKU already exists", "PRODUCT_SKU_EXISTS");

        var product = new Product
        {
            Name = request.Name,
            SKU = request.SKU,
            Barcode = request.Barcode,
            CategoryId = request.CategoryId,
            Brand = request.Brand,
            SellingPrice = request.SellingPrice,
            MRP = request.MRP,
            UOM = request.UOM,
        };

        await _unitOfWork.Repository<Product>().AddAsync(product, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return MapToDto(product);
    }

    public async Task ClearAllAsync(CancellationToken cancellationToken = default)
    {
        // DEPRECATED — kept for interface compatibility but should not be used.
        // Use ImportAsync below instead.
        throw new BusinessException("ClearAll is disabled. Use the import endpoint which now uses upsert.", "PRODUCT_CLEAR_DISABLED");
    }

    /// <summary>
    /// Upsert-based import: match by SKU, update existing products, create new ones.
    /// Existing products NOT in the import are left untouched so order history is preserved.
    /// </summary>
    public async Task<List<ProductDto>> ImportAsync(List<CreateProductRequest> requests, CancellationToken cancellationToken = default)
    {
        if (requests == null || requests.Count == 0)
            throw new BusinessException("No products provided", "PRODUCT_IMPORT_EMPTY");

        await ImportGate.WaitAsync(cancellationToken);
        try
        {

        // Normalize and validate import rows first so we fail fast with a clear business error
        // instead of surfacing a generic DB 500.
        var normalizedRequests = requests
            .Where(r => r != null)
            .Select(r =>
            {
                r.Name = r.Name?.Trim() ?? string.Empty;
                r.SKU = r.SKU?.Trim() ?? string.Empty;
                r.Barcode = string.IsNullOrWhiteSpace(r.Barcode) ? null : r.Barcode.Trim();
                r.Brand = string.IsNullOrWhiteSpace(r.Brand) ? null : r.Brand.Trim();
                r.MainCategory = string.IsNullOrWhiteSpace(r.MainCategory) ? null : r.MainCategory.Trim();
                r.SubCategory = string.IsNullOrWhiteSpace(r.SubCategory) ? null : r.SubCategory.Trim();
                r.TaxCode = string.IsNullOrWhiteSpace(r.TaxCode) ? null : r.TaxCode.Trim();
                return r;
            })
            .ToList();

        var missingSkuRows = normalizedRequests
            .Select((r, i) => new { r, row = i + 1 })
            .Where(x => string.IsNullOrWhiteSpace(x.r.SKU))
            .Select(x => x.row)
            .Take(10)
            .ToList();

        if (missingSkuRows.Count > 0)
        {
            throw new BusinessException(
                $"Import contains rows with missing SKU. Example row numbers: {string.Join(", ", missingSkuRows)}",
                "PRODUCT_IMPORT_INVALID_SKU");
        }

        // If the same SKU appears multiple times in one file, keep the last occurrence.
        // This avoids unique index violations and matches spreadsheet overwrite expectations.
        normalizedRequests = normalizedRequests
            .GroupBy(r => r.SKU, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.Last())
            .ToList();

        // helper caching dictionaries to avoid repeated DB lookups
        var mainCache = new Dictionary<string, Category>(StringComparer.OrdinalIgnoreCase);
        var subCache = new Dictionary<(string main, string sub), Category>();
        var categoryIds = new HashSet<Guid>();

        // Build caches from existing categories and extend them while importing.
        // Do not wipe categories here; import may be chunked on hosted environments.
        var allCats = await _unitOfWork.Repository<Category>().Query()
            .Include(c => c.ParentCategory)
            .ToListAsync(cancellationToken);

        foreach (var c in allCats)
        {
            categoryIds.Add(c.Id);
            if (c.ParentCategoryId == null)
                mainCache[c.Name] = c;
            else if (c.ParentCategory != null)
                subCache[(c.ParentCategory.Name.ToLowerInvariant(), c.Name.ToLowerInvariant())] = c;
        }

        // load existing products keyed by SKU for fast lookup
        var existingProducts = await _unitOfWork.Repository<Product>().Query()
            .ToListAsync(cancellationToken);
        var bySku = existingProducts
            .GroupBy(p => p.SKU, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var results = new List<ProductDto>();

        async Task<Guid?> ResolveCategoryId(CreateProductRequest r)
        {
            if (r.CategoryId.HasValue && string.IsNullOrWhiteSpace(r.SubCategory))
            {
                // Honour provided IDs only when they are known in the current category set.
                if (categoryIds.Contains(r.CategoryId.Value)) return r.CategoryId;
            }

            // try names from import if provided
            var mainName = r.MainCategory?.Trim();
            var subName = r.SubCategory?.Trim();
            if (string.IsNullOrEmpty(mainName) && string.IsNullOrEmpty(subName))
                return null;

            if (!string.IsNullOrEmpty(subName) && string.IsNullOrEmpty(mainName))
            {
                // treat sub as main if only one provided
                mainName = subName;
                subName = null;
            }

            Category? mainCat = null;
            if (!string.IsNullOrEmpty(mainName))
            {
                if (!mainCache.TryGetValue(mainName, out mainCat))
                {
                    mainCat = new Category { Name = mainName, IsActive = true };
                    await _unitOfWork.Repository<Category>().AddAsync(mainCat, cancellationToken);
                    mainCache[mainName] = mainCat;
                    categoryIds.Add(mainCat.Id);
                }
            }

            if (!string.IsNullOrEmpty(subName))
            {
                var key = ((mainName ?? string.Empty).ToLowerInvariant(), (subName ?? string.Empty).ToLowerInvariant());
                if (!subCache.TryGetValue(key, out var subCat))
                {
                    subCat = new Category { Name = subName!, ParentCategoryId = mainCat?.Id, IsActive = true };
                    await _unitOfWork.Repository<Category>().AddAsync(subCat, cancellationToken);
                    subCache[key] = subCat;
                    categoryIds.Add(subCat.Id);
                }
                return subCat.Id;
            }

            return mainCat?.Id;
        }

        foreach (var req in normalizedRequests)
        {
            if (req == null) continue; // defensive
            req.CategoryId = await ResolveCategoryId(req);

            if (bySku.TryGetValue(req.SKU, out var existing))
            {
                // UPDATE existing product
                existing.Name = req.Name;
                existing.Barcode = req.Barcode;
                existing.CategoryId = req.CategoryId;
                existing.Brand = req.Brand;
                existing.SellingPrice = req.SellingPrice;
                existing.MRP = req.MRP;
                existing.Quantity = req.Quantity;
                existing.DiscountPercent = req.DiscountPercent;
                existing.DiscountAmount = req.DiscountAmount;
                existing.TaxCode = req.TaxCode;
                existing.TaxAmount = req.TaxAmount;
                existing.TotalAmount = req.TotalAmount;
                existing.UOM = req.UOM;
                results.Add(MapToDto(existing));
            }
            else
            {
                // CREATE new product
                var product = new Product
                {
                    Name = req.Name,
                    SKU = req.SKU,
                    Barcode = req.Barcode,
                    CategoryId = req.CategoryId,
                    Brand = req.Brand,
                    SellingPrice = req.SellingPrice,
                    MRP = req.MRP,
                    Quantity = req.Quantity,
                    DiscountPercent = req.DiscountPercent,
                    DiscountAmount = req.DiscountAmount,
                    TaxCode = req.TaxCode,
                    TaxAmount = req.TaxAmount,
                    TotalAmount = req.TotalAmount,
                    UOM = req.UOM
                };
                await _unitOfWork.Repository<Product>().AddAsync(product, cancellationToken);
                bySku[req.SKU] = product;
                results.Add(MapToDto(product));
            }
        }

            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return results;
        }
        finally
        {
            ImportGate.Release();
        }
    }

    public async Task<ProductDto> UpdateAsync(Guid id, UpdateProductRequest request, CancellationToken cancellationToken = default)
    {
        var product = await _unitOfWork.Repository<Product>().Query()
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken)
            ?? throw new NotFoundException("Product", id);

        if (request.Name != null) product.Name = request.Name;
        if (request.Barcode != null) product.Barcode = request.Barcode;
        if (request.Brand != null) product.Brand = request.Brand;
        if (request.CategoryId.HasValue) product.CategoryId = request.CategoryId.Value;
        if (request.SellingPrice.HasValue) product.SellingPrice = request.SellingPrice.Value;
        if (request.MRP.HasValue) product.MRP = request.MRP.Value;
        if (request.Quantity.HasValue) product.Quantity = request.Quantity.Value;

        // metadata updates
        if (request.DiscountPercent.HasValue) product.DiscountPercent = request.DiscountPercent.Value;
        if (request.DiscountAmount.HasValue) product.DiscountAmount = request.DiscountAmount.Value;
        if (request.TaxCode != null) product.TaxCode = request.TaxCode;
        if (request.TaxAmount.HasValue) product.TaxAmount = request.TaxAmount.Value;
        if (request.TotalAmount.HasValue) product.TotalAmount = request.TotalAmount.Value;
        if (request.UOM != null) product.UOM = request.UOM;

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return MapToDto(product);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var product = await _unitOfWork.Repository<Product>().GetByIdAsync(id, cancellationToken)
            ?? throw new NotFoundException("Product", id);

        // clear any order items pointing at this product so FK won't complain
        var orderItems = await _unitOfWork.Repository<OrderItem>().Query()
            .Where(oi => oi.ProductId == id)
            .ToListAsync(cancellationToken);
        foreach (var oi in orderItems)
        {
            oi.ProductId = null;
            _unitOfWork.Repository<OrderItem>().Update(oi);
        }

        // now safely remove the product
        _unitOfWork.Repository<Product>().Remove(product);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task<int> DeleteManyAsync(List<Guid> ids, CancellationToken cancellationToken = default)
    {
        if (ids == null || ids.Count == 0)
            return 0;

        var distinctIds = ids.Distinct().ToList();
        var products = await _unitOfWork.Repository<Product>().Query()
            .Where(p => distinctIds.Contains(p.Id))
            .ToListAsync(cancellationToken);

        if (!products.Any())
            return 0;

        var productIds = products.Select(p => p.Id).ToList();

        var orderItems = await _unitOfWork.Repository<OrderItem>().Query()
            .Where(oi => oi.ProductId != null && productIds.Contains(oi.ProductId.Value))
            .ToListAsync(cancellationToken);

        foreach (var oi in orderItems)
        {
            oi.ProductId = null;
            _unitOfWork.Repository<OrderItem>().Update(oi);
        }

        foreach (var product in products)
        {
            _unitOfWork.Repository<Product>().Remove(product);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return products.Count;
    }

    public async Task<int> CleanupOrphanCategoriesAsync(CancellationToken cancellationToken = default)
    {
        var usedCategoryIds = await _unitOfWork.Repository<Product>().Query()
            .Where(p => p.CategoryId != null)
            .Select(p => p.CategoryId!.Value)
            .Distinct()
            .ToListAsync(cancellationToken);

        var orphanSubCategories = await _unitOfWork.Repository<Category>().Query()
            .Where(c => c.ParentCategoryId != null)
            .Where(c => !usedCategoryIds.Contains(c.Id))
            .ToListAsync(cancellationToken);

        foreach (var sub in orphanSubCategories)
        {
            _unitOfWork.Repository<Category>().Remove(sub);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        var orphanParentCategories = await _unitOfWork.Repository<Category>().Query()
            .Where(c => c.ParentCategoryId == null)
            .Where(c => !usedCategoryIds.Contains(c.Id))
            .Where(c => !_unitOfWork.Repository<Category>().Query().Any(child => child.ParentCategoryId == c.Id))
            .ToListAsync(cancellationToken);

        foreach (var parent in orphanParentCategories)
        {
            _unitOfWork.Repository<Category>().Remove(parent);
        }

        if (orphanParentCategories.Any())
            await _unitOfWork.SaveChangesAsync(cancellationToken);

        return orphanSubCategories.Count + orphanParentCategories.Count;
    }

    public async Task<List<ProductDto>> ReplaceAllAsync(List<CreateProductRequest> requests, CancellationToken cancellationToken = default)
    {
        if (requests == null || !requests.Any())
            throw new BusinessException("No products provided", "PRODUCT_IMPORT_EMPTY");

        var allProductIds = await _unitOfWork.Repository<Product>().Query()
            .Select(p => p.Id)
            .ToListAsync(cancellationToken);

        if (allProductIds.Any())
            await DeleteManyAsync(allProductIds, cancellationToken);

        await CleanupOrphanCategoriesAsync(cancellationToken);

        return await ImportAsync(requests, cancellationToken);
    }

    public async Task<ProductDto> UpdatePriceAsync(Guid id, UpdatePriceRequest request, CancellationToken cancellationToken = default)
    {
        var product = await _unitOfWork.Repository<Product>().Query()
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken)
            ?? throw new NotFoundException("Product", id);

        product.SellingPrice = request.NewPrice;
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return MapToDto(product);
    }




    public async Task<List<CategoryDto>> GetCategoriesAsync(CancellationToken cancellationToken = default)
    {
        var categories = await _unitOfWork.Repository<Category>().Query()
            .Where(c => c.ParentCategoryId == null && c.IsActive)
            .Include(c => c.SubCategories)
            .OrderBy(c => c.SortOrder)
            .ToListAsync(cancellationToken);

        return categories.Select(MapCategoryToDto).ToList();
    }

    public async Task<CategoryDto> CreateCategoryAsync(CreateCategoryRequest request, CancellationToken cancellationToken = default)
    {
        var category = new Category
        {
            Name = request.Name,
            Description = request.Description,
            ParentCategoryId = request.ParentCategoryId,
            SortOrder = request.SortOrder,
            IsActive = true
        };

        await _unitOfWork.Repository<Category>().AddAsync(category, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return MapCategoryToDto(category);
    }

    private static ProductDto MapToDto(Product p) => new()
    {
        Id = p.Id,
        Name = p.Name,
        SKU = p.SKU,
        Barcode = p.Barcode,
        CategoryId = p.CategoryId,
        CategoryName = p.Category?.Name,
        Brand = p.Brand,
        SellingPrice = p.SellingPrice,
        MRP = p.MRP,
        Quantity = p.Quantity,
        // include import metadata so the frontend can display exactly what was in the sheet
        DiscountPercent = p.DiscountPercent,
        DiscountAmount = p.DiscountAmount,
        TaxCode = p.TaxCode,
        TaxAmount = p.TaxAmount,
        TotalAmount = p.TotalAmount,
        UOM = p.UOM,
        ImageUrls = DeserializeImages(p.ImageUrlsJson),
        CreatedAt = p.CreatedAt
    };

    private static List<string> DeserializeImages(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new List<string>();
        try { return JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>(); }
        catch { return new List<string>(); }
    }

    public async Task<ProductDto> AddImageAsync(Guid id, string imageUrl, CancellationToken cancellationToken = default)
    {
        var product = await _unitOfWork.Repository<Product>().GetByIdAsync(id, cancellationToken)
            ?? throw new NotFoundException("Product", id);
        var images = DeserializeImages(product.ImageUrlsJson);
        images.Add(imageUrl);
        product.ImageUrlsJson = JsonSerializer.Serialize(images);
        product.UpdatedAt = DateTime.UtcNow;
        _unitOfWork.Repository<Product>().Update(product);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return MapToDto(product);
    }

    public async Task<ProductDto> RemoveImageAsync(Guid id, int index, CancellationToken cancellationToken = default)
    {
        var product = await _unitOfWork.Repository<Product>().GetByIdAsync(id, cancellationToken)
            ?? throw new NotFoundException("Product", id);
        var images = DeserializeImages(product.ImageUrlsJson);
        if (index >= 0 && index < images.Count)
            images.RemoveAt(index);
        product.ImageUrlsJson = images.Count > 0 ? JsonSerializer.Serialize(images) : null;
        product.UpdatedAt = DateTime.UtcNow;
        _unitOfWork.Repository<Product>().Update(product);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return MapToDto(product);
    }

    private static CategoryDto MapCategoryToDto(Category c) => new()
    {
        Id = c.Id,
        Name = c.Name,
        Description = c.Description,
        ParentCategoryId = c.ParentCategoryId,
        SortOrder = c.SortOrder,
        IsActive = c.IsActive,
        SubCategories = c.SubCategories?.Select(MapCategoryToDto).ToList() ?? []
    };
}

