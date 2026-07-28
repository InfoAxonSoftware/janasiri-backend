namespace DistributionSystem.Application.DTOs.Cart;

public class CartItemDto
{
    public Guid Id { get; set; }
    public Guid ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string? ProductImage { get; set; }
    public decimal UnitPrice { get; set; }
    public int Quantity { get; set; }
    public decimal LineTotal { get; set; }
}

public class AddToCartRequest
{
    public Guid ProductId { get; set; }
    public int Quantity { get; set; }
}

public class UpdateCartRequest
{
    public Guid ProductId { get; set; }
    public int Quantity { get; set; }
}
