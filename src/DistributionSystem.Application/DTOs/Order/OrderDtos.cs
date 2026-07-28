using DistributionSystem.Domain.Enums;
using DistributionSystem.Application.DTOs.QuickRequests;

namespace DistributionSystem.Application.DTOs.Order;

public class OrderDto
{
    public Guid Id { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public Guid CustomerId { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string? ShopName { get; set; }
    public Guid? RepId { get; set; }
    public string? RepName { get; set; }
    public DateTime OrderDate { get; set; }
    public DateTime? RequiredDeliveryDate { get; set; }
    public DateTime? ActualDeliveryDate { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal SubTotal { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal TotalAmount { get; set; }
    public string? DeliveryAddress { get; set; }
    public string? DeliveryNotes { get; set; }
    public int? Rating { get; set; }
    public bool IsFromApprovedQuotation { get; set; }
    public string? SourceQuotationNumber { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public List<OrderItemDto> Items { get; set; } = [];
    public DateTime CreatedAt { get; set; }
}

public class OrderItemDto
{
    public Guid Id { get; set; }
    public Guid? ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string? ProductSKU { get; set; }
    public string? TaxCode { get; set; }
    public int Quantity { get; set; }
    public int BackorderedQuantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal? MRP { get; set; }
    public decimal DiscountPercent { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal LineTotal { get; set; }
}

public class CreateOrderRequest
{
    public Guid CustomerId { get; set; }
    public DateTime? RequiredDeliveryDate { get; set; }
    public string? DeliveryAddress { get; set; }
    public string? DeliveryNotes { get; set; }
    public List<CreateOrderItemRequest> Items { get; set; } = [];
}

public class CreateOrderItemRequest
{
    public Guid ProductId { get; set; }
    public int Quantity { get; set; }
    public decimal? DiscountPercent { get; set; }
}

public class UpdateOrderStatusRequest
{
    public OrderStatus Status { get; set; }
    public string? Reason { get; set; }
}

public class OrderFilterRequest
{
    public OrderStatus? Status { get; set; }
    public Guid? CustomerId { get; set; }
    public Guid? RepId { get; set; }
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}

public class UnifiedOrderFilterRequest
{
    private int _page = 1;
    private int _pageSize = 20;

    public int Page { get => _page; set => _page = Math.Max(1, value); }
    public int PageSize { get => _pageSize; set => _pageSize = Math.Clamp(value, 1, 100); }
    public string? Search { get; set; }
    public string? Status { get; set; }
    public Guid? CustomerId { get; set; }
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
    public string SortField { get; set; } = "date";
    public string SortDirection { get; set; } = "desc";
}

public class UnifiedOrderDto
{
    public Guid Id { get; set; }
    public string Kind { get; set; } = "Order";
    public string Number { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public string? ShopName { get; set; }
    public Guid? CustomerId { get; set; }
    public Guid? RepId { get; set; }
    public string? RepName { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime Date { get; set; }
    public DateTime CreatedAt { get; set; }
    public decimal? TotalAmount { get; set; }
    public DateTime? DeletedAt { get; set; }
    public OrderDto? Order { get; set; }
    public QuickRequestDto? QuickOrder { get; set; }
}

public class OrderTrashItemRequest
{
    public Guid Id { get; set; }
    public string Kind { get; set; } = string.Empty;
}

public class OrderTrashItemsRequest
{
    public List<OrderTrashItemRequest> Items { get; set; } = [];
}

public class RateOrderRequest
{
    public int Rating { get; set; }
    public string? Comment { get; set; }
}
