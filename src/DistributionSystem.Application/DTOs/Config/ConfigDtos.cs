namespace DistributionSystem.Application.DTOs.Config;

public class SystemConfigDto
{
    public Guid Id { get; set; }
    public string CompanyName { get; set; } = string.Empty;
    public string? CompanyLogo { get; set; }
    public string? CompanyAddress { get; set; }
    public string? CompanyPhone { get; set; }
    public string? CompanyEmail { get; set; }
    public string? TaxNumber { get; set; }
    public string Currency { get; set; } = "LKR";
    public string? BrandPrimaryColor { get; set; }
    public string? BrandSecondaryColor { get; set; }
    public bool RequireCustomerApproval { get; set; }
    public bool RequireQuotationApproval { get; set; }
    public int DefaultPaymentTermsDays { get; set; }
    public decimal DefaultCreditLimit { get; set; }
}

public class UpdateSystemConfigRequest
{
    public string? CompanyName { get; set; }
    public string? CompanyLogo { get; set; }
    public string? CompanyAddress { get; set; }
    public string? CompanyPhone { get; set; }
    public string? CompanyEmail { get; set; }
    public string? TaxNumber { get; set; }
    public string? Currency { get; set; }
    public string? BrandPrimaryColor { get; set; }
    public string? BrandSecondaryColor { get; set; }
    public bool? RequireCustomerApproval { get; set; }
    public bool? RequireQuotationApproval { get; set; }
    public int? DefaultPaymentTermsDays { get; set; }
    public decimal? DefaultCreditLimit { get; set; }
}
