
namespace DistributionSystem.Application.DTOs.Product;

public class ProductDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string SKU { get; set; } = string.Empty;
    public string? Barcode { get; set; }
    public Guid? CategoryId { get; set; }
    public string? CategoryName { get; set; }
    public string? Brand { get; set; }
    public decimal SellingPrice { get; set; }
    public decimal? MRP { get; set; }
    public int Quantity { get; set; }
    
    // discount / tax info from sheet
    public decimal? DiscountPercent { get; set; }
    public decimal? DiscountAmount { get; set; }
    public string? TaxCode { get; set; }
    public decimal? TaxAmount { get; set; }
    public decimal? TotalAmount { get; set; }
    public string? UOM { get; set; }
    public List<string> ImageUrls { get; set; } = new();
    public DateTime CreatedAt { get; set; }
}

public class CreateProductRequest
{
    public string Name { get; set; } = string.Empty;
    public string SKU { get; set; } = string.Empty;
    public string? Barcode { get; set; }
    public Guid? CategoryId { get; set; }
    public string? Brand { get; set; }
    public decimal SellingPrice { get; set; }
    public decimal? MRP { get; set; }
    public int Quantity { get; set; }    // hierarchical categories (used only during import when names are provided)
    public string? MainCategory { get; set; }
    public string? SubCategory { get; set; }    // discount and tax info (optional, used when importing from external sheet)
    public decimal? DiscountPercent { get; set; }
    public decimal? DiscountAmount { get; set; }
    public string? TaxCode { get; set; }
    public decimal? TaxAmount { get; set; }
    public decimal? TotalAmount { get; set; }
    public string? UOM { get; set; }
}

public class UpdateProductRequest
{
    public string? Name { get; set; }
    public string? Barcode { get; set; }
    public Guid? CategoryId { get; set; }
    public string? Brand { get; set; }
    public decimal? SellingPrice { get; set; }
    public decimal? MRP { get; set; }
    public int? Quantity { get; set; }

    // import-specific/optional fields
    public decimal? DiscountPercent { get; set; }
    public decimal? DiscountAmount { get; set; }
    public string? TaxCode { get; set; }
    public decimal? TaxAmount { get; set; }
    public decimal? TotalAmount { get; set; }
    public string? UOM { get; set; }

}

public class ImportProductsRequest
{
    public List<CreateProductRequest> Requests { get; set; } = new();
}

public class BulkDeleteProductsRequest
{
    public List<Guid> Ids { get; set; } = new();
}

public class BulkDeleteProductsResponse
{
    public int Deleted { get; set; }
    public int Missing { get; set; }
}

public class UpdatePriceRequest
{
    public decimal NewPrice { get; set; }
}


public class CategoryDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public Guid? ParentCategoryId { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; }
    public List<CategoryDto> SubCategories { get; set; } = [];
}

// Product listing filter request used by ProductService.GetAllAsync
public class ProductFilterRequest
{
    public string? Search { get; set; }
    public Guid? CategoryId { get; set; }
    public string? Brand { get; set; }
    public decimal? MinPrice { get; set; }
    public decimal? MaxPrice { get; set; }
    public string SortBy { get; set; } = "name"; // name | price | createdAt
    public string SortDir { get; set; } = "asc"; // asc | desc
}
public class CreateCategoryRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public Guid? ParentCategoryId { get; set; }
    public int SortOrder { get; set; }
}

