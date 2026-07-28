using DistributionSystem.Domain.Interfaces;

namespace DistributionSystem.Domain.Entities;

public class SystemConfiguration : IAuditableEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string CompanyName { get; set; } = string.Empty;
    public string? CompanyLogo { get; set; }
    public string? CompanyAddress { get; set; }
    public string? CompanyPhone { get; set; }
    public string? CompanyEmail { get; set; }
    public string? TaxNumber { get; set; }
    public string Currency { get; set; } = "LKR";
    public string? BrandPrimaryColor { get; set; }
    public string? BrandSecondaryColor { get; set; }
    public bool RequireCustomerApproval { get; set; } = true;
    public bool RequireQuotationApproval { get; set; } = true;
    public int DefaultPaymentTermsDays { get; set; } = 30;
    public decimal DefaultCreditLimit { get; set; } = 0;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public string? UpdatedBy { get; set; }
}
