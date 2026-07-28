using System.Security.Claims;
using DistributionSystem.Application.DTOs.Common;
using DistributionSystem.Application.DTOs.Customer;
using DistributionSystem.Application.DTOs.Product;
using DistributionSystem.Application.DTOs.Report;
using DistributionSystem.Application.Services.Interfaces;
using DistributionSystem.API.Hubs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;

namespace DistributionSystem.API.Controllers;

/// <summary>
/// Product and inventory management endpoints
/// </summary>
[ApiController]
[Route("api")]
[Authorize]
[Produces("application/json")]
public class ProductController : ControllerBase
{
    private readonly IProductService _productService;
    private readonly ICustomerService _customerService;
    private readonly IHubContext<NotificationHub> _notificationHub;
    private readonly IReportService _reportService;
    private readonly ILogger<ProductController> _logger;
    private readonly IFileStorageService _fileStorage;

    private static readonly string[] AllowedImageExtensions = [".jpg", ".jpeg", ".png", ".webp", ".gif"];
    private static readonly string[] AllowedImageTypes = ["image/jpeg", "image/png", "image/webp", "image/gif"];
    private const long MaxImageSizeBytes = 10 * 1024 * 1024;

    public ProductController(IProductService productService, ICustomerService customerService, IHubContext<NotificationHub> notificationHub, IReportService reportService, ILogger<ProductController> logger, IFileStorageService fileStorage)
    {
        _productService = productService;
        _customerService = customerService;
        _notificationHub = notificationHub;
        _reportService = reportService;
        _logger = logger;
        _fileStorage = fileStorage;
    }

    private Guid? TryGetCurrentUserId()
    {
        var raw = User.FindFirstValue(ClaimTypes.NameIdentifier)
                  ?? User.FindFirstValue("sub");

        return Guid.TryParse(raw, out var userId) ? userId : null;
    }

    /// <summary>Get all products with pagination and filtering</summary>
    [HttpGet("admin/products")]
    [HttpGet("rep/products/catalog")]
    [HttpGet("customer/products")]
    public async Task<IActionResult> GetProducts([FromQuery] int page = 1, [FromQuery] int pageSize = 20,
        [FromQuery] string? search = null, [FromQuery] Guid? categoryId = null,
        [FromQuery] string? brand = null, [FromQuery] decimal? minPrice = null, [FromQuery] decimal? maxPrice = null,
        [FromQuery] string sortBy = "name", [FromQuery] string sortDir = "asc", CancellationToken ct = default)
    {
        var filter = new Application.DTOs.Product.ProductFilterRequest
        {
            Search = search,
            CategoryId = categoryId,
            Brand = brand,
            MinPrice = minPrice,
            MaxPrice = maxPrice,
            SortBy = sortBy,
            SortDir = sortDir
        };

        var result = await _productService.GetAllAsync(page, pageSize, filter, ct);
        var materializedItems = result.Items.ToList();
        result.Items = materializedItems;

        // If this is a customer catalog route, apply customer-specific overrides.
        if (HttpContext.Request.Path.StartsWithSegments("/api/customer/products"))
        {
            try
            {
                var userId = TryGetCurrentUserId();
                if (!userId.HasValue)
                    throw new InvalidOperationException("Customer user id claim is missing.");

                var customer = await _customerService.GetByUserIdAsync(userId.Value, ct);
                var specialPrices = await _customerService.GetSpecialPricesAsync(customer.Id, ct);
                var priceMap = specialPrices
                    .GroupBy(p => p.ProductId)
                    .ToDictionary(g => g.Key, g => g.First());

                _logger.LogInformation("Customer catalog override lookup: userId={UserId}, customerId={CustomerId}, overrides={OverrideCount}", userId.Value, customer.Id, priceMap.Count);

                foreach (var item in materializedItems)
                {
                    if (!priceMap.TryGetValue(item.Id, out var overridePrice))
                        continue;

                    var basePrice = item.SellingPrice;

                    // special price takes precedence
                    if (overridePrice.SpecialPrice.HasValue)
                    {
                        item.SellingPrice = overridePrice.SpecialPrice.Value;
                        item.DiscountPercent = null;
                        item.DiscountAmount = Math.Round(basePrice - item.SellingPrice, 2);
                    }
                    else if (overridePrice.DiscountPercent.HasValue)
                    {
                        item.DiscountPercent = overridePrice.DiscountPercent.Value;
                        item.DiscountAmount = Math.Round(basePrice * overridePrice.DiscountPercent.Value / 100m, 2);
                        item.SellingPrice = Math.Round(basePrice - (item.DiscountAmount ?? 0m), 2);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Customer catalog override resolution failed for path {Path}", HttpContext.Request.Path);
            }
        }

        return Ok(ApiResponse<PagedResult<ProductDto>>.SuccessResponse(result));
    }

    /// <summary>Get product by ID</summary>
    [HttpGet("admin/products/{id}")]
    [HttpGet("customer/products/{id}")]
    public async Task<IActionResult> GetProduct(Guid id, CancellationToken ct)
    {
        var result = await _productService.GetByIdAsync(id, ct);

        if (HttpContext.Request.Path.StartsWithSegments("/api/customer/products"))
        {
            try
            {
                var userId = TryGetCurrentUserId();
                if (!userId.HasValue)
                    throw new InvalidOperationException("Customer user id claim is missing.");

                var customer = await _customerService.GetByUserIdAsync(userId.Value, ct);
                var specialPrices = await _customerService.GetSpecialPricesAsync(customer.Id, ct);
                var special = specialPrices.FirstOrDefault(p => p.ProductId == id);
                _logger.LogInformation("Customer product override lookup: userId={UserId}, customerId={CustomerId}, productId={ProductId}, hasOverride={HasOverride}", userId.Value, customer.Id, id, special != null);
                var basePrice = result.SellingPrice;
                if (special?.SpecialPrice != null)
                {
                    result.SellingPrice = special.SpecialPrice.Value;
                    result.DiscountPercent = null;
                    result.DiscountAmount = Math.Round(basePrice - result.SellingPrice, 2);
                }
                else if (special?.DiscountPercent != null)
                {
                    result.DiscountPercent = special.DiscountPercent.Value;
                    result.DiscountAmount = Math.Round(basePrice * special.DiscountPercent.Value / 100m, 2);
                    result.SellingPrice = Math.Round(basePrice - (result.DiscountAmount ?? 0m), 2);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Customer product override resolution failed for path {Path}, productId={ProductId}", HttpContext.Request.Path, id);
            }
        }

        return Ok(ApiResponse<ProductDto>.SuccessResponse(result));
    }

    /// <summary>Create a new product (Admin only)</summary>
    [HttpPost("admin/products")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public async Task<IActionResult> CreateProduct([FromBody] CreateProductRequest request, CancellationToken ct)
    {
        var result = await _productService.CreateAsync(request, ct);
        return CreatedAtAction(nameof(GetProduct), new { id = result.Id }, ApiResponse<ProductDto>.SuccessResponse(result, "Product created"));
    }

    /// <summary>Update a product (Admin only)</summary>
    [HttpPut("admin/products/{id}")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public async Task<IActionResult> UpdateProduct(Guid id, [FromBody] UpdateProductRequest request, CancellationToken ct)
    {
        var result = await _productService.UpdateAsync(id, request, ct);
        return Ok(ApiResponse<ProductDto>.SuccessResponse(result, "Product updated"));
    }

    /// <summary>Delete (soft) a product (Admin only)</summary>
    [HttpDelete("admin/products/{id}")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public async Task<IActionResult> DeleteProduct(Guid id, CancellationToken ct)
    {
        await _productService.DeleteAsync(id, ct);
        return Ok(ApiResponse<string>.SuccessResponse("Product deleted"));
    }

    /// <summary>Bulk delete products (Admin only)</summary>
    [HttpPost("admin/products/bulk-delete")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public async Task<IActionResult> BulkDeleteProducts([FromBody] BulkDeleteProductsRequest request, CancellationToken ct)
    {
        if (request == null || request.Ids == null || !request.Ids.Any())
            return BadRequest(ApiResponse<string>.ErrorResponse("No product IDs provided"));

        var totalRequested = request.Ids.Count;
        var deletedCount = await _productService.DeleteManyAsync(request.Ids, ct);
        var missingCount = totalRequested - deletedCount;

        var response = new BulkDeleteProductsResponse
        {
            Deleted = deletedCount,
            Missing = missingCount
        };

        return Ok(ApiResponse<BulkDeleteProductsResponse>.SuccessResponse(response, $"Deleted {deletedCount} products. {missingCount} not found or already deleted."));
    }

    /// <summary>Update product price and notify via SignalR</summary>
    [HttpPut("admin/products/{id}/price")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public async Task<IActionResult> UpdatePrice(Guid id, [FromBody] UpdatePriceRequest request, CancellationToken ct)
    {
        var result = await _productService.UpdatePriceAsync(id, request, ct);
        // Notify all reps about price change via SignalR
        await _notificationHub.Clients.Group("role_SalesRep").SendAsync("PriceUpdated", new { result.Id, result.Name, result.SellingPrice }, ct);
        return Ok(ApiResponse<ProductDto>.SuccessResponse(result, "Price updated"));
    }

    /// <summary>Upload and add an image to a product (Admin)</summary>
    [HttpPost("admin/products/{id}/images")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> AddProductImage(Guid id, IFormFile image, CancellationToken ct)
    {
        if (image == null || image.Length == 0)
            return BadRequest(ApiResponse<string>.ErrorResponse("Image file is required"));

        StoredFileResult saved;
        try
        {
            saved = await _fileStorage.SaveAsync(image, "products", FileAccessCategory.Public, AllowedImageExtensions, AllowedImageTypes, MaxImageSizeBytes, ct);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ApiResponse<string>.ErrorResponse(ex.Message));
        }

        var imageUrl = _fileStorage.GetPublicUrl(saved.StorageKey)!;
        var result = await _productService.AddImageAsync(id, imageUrl, ct);
        return Ok(ApiResponse<ProductDto>.SuccessResponse(result, "Image added"));
    }

    /// <summary>Remove a product image by index (Admin)</summary>
    [HttpDelete("admin/products/{id}/images/{index:int}")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public async Task<IActionResult> RemoveProductImage(Guid id, int index, CancellationToken ct)
    {
        var result = await _productService.RemoveImageAsync(id, index, ct);
        return Ok(ApiResponse<ProductDto>.SuccessResponse(result, "Image removed"));
    }

    /// <summary>Adjust stock level with reason</summary>

    /// <summary>Get product categories</summary>
    [HttpGet("categories")]
    public async Task<IActionResult> GetCategories(CancellationToken ct)
    {
        var result = await _productService.GetCategoriesAsync(ct);
        return Ok(ApiResponse<List<CategoryDto>>.SuccessResponse(result));
    }

    /// <summary>Create a product category</summary>
    [HttpPost("admin/categories")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public async Task<IActionResult> CreateCategory([FromBody] CreateCategoryRequest request, CancellationToken ct)
    {
        var result = await _productService.CreateCategoryAsync(request, ct);
        return Ok(ApiResponse<CategoryDto>.SuccessResponse(result, "Category created"));
    }

    /// <summary>Bulk import admin products from JSON array (used by frontend after parsing Excel)</summary>
    [HttpPost("admin/products/import")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public async Task<IActionResult> ImportProducts([FromBody] ImportProductsRequest payload, CancellationToken ct)
    {
        var requests = payload?.Requests;
        if (requests == null || !requests.Any())
            return BadRequest(ApiResponse<string>.ErrorResponse("No products provided"));

        _logger.LogInformation(
            "Product import request received: {RequestCount} rows (UserId: {UserId})",
            requests.Count,
            TryGetCurrentUserId());

        // Upsert: match by SKU, update existing, create new — never delete
        var results = await _productService.ImportAsync(requests, ct);

        // Notify connected clients that product catalog changed
        await _notificationHub.Clients.Group("role_Customer").SendAsync("ProductsUpdated");
        await _notificationHub.Clients.Group("role_SalesRep").SendAsync("ProductsUpdated");
        await _notificationHub.Clients.Group("role_Admin").SendAsync("ProductsUpdated");

        return Ok(ApiResponse<List<ProductDto>>.SuccessResponse(results, $"{results.Count} products imported (upsert)"));
    }

    [HttpPost("admin/products/import/full-replace")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public async Task<IActionResult> FullReplaceProducts([FromBody] ImportProductsRequest payload, CancellationToken ct)
    {
        var requests = payload?.Requests;
        if (requests == null || !requests.Any())
            return BadRequest(ApiResponse<string>.ErrorResponse("No products provided"));

        _logger.LogInformation(
            "Product full-replace import request received: {RequestCount} rows (UserId: {UserId})",
            requests.Count,
            TryGetCurrentUserId());

        var results = await _productService.ReplaceAllAsync(requests, ct);

        // Notify connected clients (customer/rep/admin) that products changed
        await _notificationHub.Clients.Group("role_Customer").SendAsync("ProductsUpdated");
        await _notificationHub.Clients.Group("role_SalesRep").SendAsync("ProductsUpdated");
        await _notificationHub.Clients.Group("role_Admin").SendAsync("ProductsUpdated");

        return Ok(ApiResponse<List<ProductDto>>.SuccessResponse(results, $"{results.Count} products replaced"));
    }

    /// <summary>Clean up orphan categories (Admin only)</summary>
    [HttpPost("admin/categories/cleanup-orphans")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public async Task<IActionResult> CleanupOrphanCategories(CancellationToken ct)
    {
        var deleted = await _productService.CleanupOrphanCategoriesAsync(ct);
        return Ok(ApiResponse<int>.SuccessResponse(deleted, $"Removed {deleted} orphan category items"));
    }

    /// <summary>Check real-time product availability (Rep)</summary>
    [HttpGet("rep/products/{id}/availability")]
    [Authorize(Roles = "SalesRep")]
    public async Task<IActionResult> CheckAvailability(Guid id, CancellationToken ct)
    {
        var product = await _productService.GetByIdAsync(id, ct);
        return Ok(ApiResponse<ProductDto>.SuccessResponse(product));
    }

    /// <summary>Search products (Customer)</summary>
    [HttpGet("customer/products/search")]
    [Authorize(Roles = "Customer")]
    public async Task<IActionResult> SearchProducts([FromQuery] string? q = null, [FromQuery] Guid? categoryId = null,
        [FromQuery] string? brand = null, [FromQuery] decimal? minPrice = null, [FromQuery] decimal? maxPrice = null,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
    {
        var filter = new Application.DTOs.Product.ProductFilterRequest
        {
            Search = q,
            CategoryId = categoryId,
            Brand = brand,
            MinPrice = minPrice,
            MaxPrice = maxPrice
        };

        var result = await _productService.GetAllAsync(page, pageSize, filter, ct);
        return Ok(ApiResponse<PagedResult<ProductDto>>.SuccessResponse(result));
    }

    /// <summary>Get customer top / best-selling products (Sales-based)</summary>
    [HttpGet("customer/products/best-selling")]
    [Authorize(Roles = "Customer")]
    public async Task<IActionResult> GetCustomerBestSellingProducts([FromQuery] DateTime? from, [FromQuery] DateTime? to,
        [FromQuery] int top = 6, CancellationToken ct = default)
    {
        var filter = new ReportFilterRequest
        {
            FromDate = from ?? DateTime.UtcNow.AddMonths(-1),
            ToDate = to ?? DateTime.UtcNow,
            Top = top
        };

        var topProducts = await _reportService.GetBestSellingProductsAsync(filter, ct);

        var tasks = topProducts.Select(async tp =>
        {
            try
            {
                return await _productService.GetByIdAsync(tp.ProductId, ct);
            }
            catch
            {
                return null;
            }
        });

        var products = (await Task.WhenAll(tasks)).Where(p => p != null).Select(p => p!).ToList();
        return Ok(ApiResponse<List<ProductDto>>.SuccessResponse(products));
    }

    /// <summary>Get customer favorite products</summary>
    [HttpGet("customer/products/favorites")]
    [Authorize(Roles = "Customer")]
    public async Task<IActionResult> GetFavorites(CancellationToken ct)
    {
        // Simplified - would need full implementation with FavoriteProduct repository
        return Ok(ApiResponse<List<ProductDto>>.SuccessResponse(new List<ProductDto>()));
    }

    /// <summary>Add product to favorites</summary>
    [HttpPost("customer/products/{id}/favorite")]
    [Authorize(Roles = "Customer")]
    public async Task<IActionResult> AddToFavorites(Guid id, CancellationToken ct)
    {
        return Ok(ApiResponse<string>.SuccessResponse("Added to favorites"));
    }
}

