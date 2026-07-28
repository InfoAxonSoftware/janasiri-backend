namespace DistributionSystem.Application.DTOs.Config;

public class SpecialOfferDto
{
    public Guid Id { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string OfferBrief { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class CreateSpecialOfferRequest
{
    public string ProductName { get; set; } = string.Empty;
    public string OfferBrief { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
}

public class UpdateSpecialOfferRequest
{
    public string ProductName { get; set; } = string.Empty;
    public string OfferBrief { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
}