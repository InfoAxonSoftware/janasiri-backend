namespace DistributionSystem.Application.DTOs.Quotation;

public class QuotationDto
{
    public Guid Id { get; set; }
    public string QuotationNumber { get; set; } = string.Empty;
    public Guid CustomerId { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string? ShopName { get; set; }
    public Guid? RepId { get; set; }
    public string? RepName { get; set; }
    public Guid? CoordinatorId { get; set; }
    public string? CoordinatorName { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal SubTotal { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal TotalAmount { get; set; }
    public string? Notes { get; set; }
    public string? RejectionReason { get; set; }
    public DateTime? ValidUntil { get; set; }
    public Guid? ConvertedOrderId { get; set; }
    public List<QuotationItemDto> Items { get; set; } = [];
    public DateTime CreatedAt { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
}

public class QuotationItemDto
{
    public Guid Id { get; set; }
    public Guid? ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string? ProductSKU { get; set; }
    public decimal? MRP { get; set; }
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal? ExpectedPrice { get; set; }
    public decimal DiscountPercent { get; set; }
    public string? TaxCode { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal LineTotal { get; set; }
}

public class CreateQuotationRequest
{
    public Guid CustomerId { get; set; }
    public string? Notes { get; set; }
    public DateTime? ValidUntil { get; set; }
    public List<CreateQuotationItemRequest> Items { get; set; } = [];
}

public class CreateQuotationItemRequest
{
    public Guid ProductId { get; set; }
    public int Quantity { get; set; }
    public decimal? ExpectedPrice { get; set; }
    public decimal DiscountPercent { get; set; }
}

public class ApproveQuotationRequest
{
    public string? Notes { get; set; }
}

public class RejectQuotationRequest
{
    public string Reason { get; set; } = string.Empty;
}

public class ConvertQuotationToOrderRequest
{
    public string? DeliveryAddress { get; set; }
    public string? DeliveryNotes { get; set; }
    public DateTime? RequiredDeliveryDate { get; set; }
}

public class CancelQuotationRequest
{
    public string? Reason { get; set; }
}
