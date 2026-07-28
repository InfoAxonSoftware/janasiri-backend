using DistributionSystem.Application.DTOs.Common;
using DistributionSystem.Application.DTOs.Config;
using DistributionSystem.Application.Exceptions;
using DistributionSystem.Application.Services.Interfaces;
using DistributionSystem.Domain.Entities;
using DistributionSystem.Infrastructure.Data.Repositories.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace DistributionSystem.Application.Services.Implementations;

public class SystemConfigService : ISystemConfigService
{
    private readonly IUnitOfWork _unitOfWork;

    public SystemConfigService(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<SystemConfigDto> GetConfigAsync(CancellationToken ct)
    {
        var config = await _unitOfWork.Repository<SystemConfiguration>().Query()
            .FirstOrDefaultAsync(ct);

        if (config == null)
        {
            // Create default config
            config = new SystemConfiguration
            {
                CompanyName = "Distribution System",
                Currency = "LKR",
                RequireCustomerApproval = true,
                RequireQuotationApproval = true,
                DefaultPaymentTermsDays = 30,
            };
            await _unitOfWork.Repository<SystemConfiguration>().AddAsync(config, ct);
            await _unitOfWork.SaveChangesAsync(ct);
        }

        return MapToDto(config);
    }

    public async Task<SystemConfigDto> UpdateConfigAsync(UpdateSystemConfigRequest request, CancellationToken ct)
    {
        if (request.CompanyName is not null && string.IsNullOrWhiteSpace(request.CompanyName))
            throw new BusinessException("Company name cannot be empty", "CONFIG_COMPANY_NAME_REQUIRED");

        if (request.DefaultPaymentTermsDays.HasValue && (request.DefaultPaymentTermsDays.Value < 1 || request.DefaultPaymentTermsDays.Value > 365))
            throw new BusinessException("Default payment terms must be between 1 and 365 days", "CONFIG_PAYMENT_TERMS_INVALID");

        if (request.DefaultCreditLimit.HasValue && request.DefaultCreditLimit.Value < 0)
            throw new BusinessException("Default credit limit must be zero or greater", "CONFIG_CREDIT_LIMIT_INVALID");

        var config = await _unitOfWork.Repository<SystemConfiguration>().Query()
            .FirstOrDefaultAsync(ct);

        if (config == null)
        {
            config = new SystemConfiguration();
            await _unitOfWork.Repository<SystemConfiguration>().AddAsync(config, ct);
        }

        if (request.CompanyName != null) config.CompanyName = request.CompanyName;
        if (request.CompanyLogo != null) config.CompanyLogo = request.CompanyLogo;
        if (request.CompanyAddress != null) config.CompanyAddress = request.CompanyAddress;
        if (request.CompanyPhone != null) config.CompanyPhone = request.CompanyPhone;
        if (request.CompanyEmail != null) config.CompanyEmail = request.CompanyEmail;
        if (request.TaxNumber != null) config.TaxNumber = request.TaxNumber;
        if (request.Currency != null) config.Currency = request.Currency;
        if (request.BrandPrimaryColor != null) config.BrandPrimaryColor = request.BrandPrimaryColor;
        if (request.BrandSecondaryColor != null) config.BrandSecondaryColor = request.BrandSecondaryColor;
        if (request.RequireCustomerApproval.HasValue) config.RequireCustomerApproval = request.RequireCustomerApproval.Value;
        if (request.RequireQuotationApproval.HasValue) config.RequireQuotationApproval = request.RequireQuotationApproval.Value;
        if (request.DefaultPaymentTermsDays.HasValue) config.DefaultPaymentTermsDays = request.DefaultPaymentTermsDays.Value;
        if (request.DefaultCreditLimit.HasValue) config.DefaultCreditLimit = request.DefaultCreditLimit.Value;

        _unitOfWork.Repository<SystemConfiguration>().Update(config);
        await _unitOfWork.SaveChangesAsync(ct);

        return MapToDto(config);
    }

    private static SystemConfigDto MapToDto(SystemConfiguration c) => new()
    {
        Id = c.Id,
        CompanyName = c.CompanyName,
        CompanyLogo = c.CompanyLogo,
        CompanyAddress = c.CompanyAddress,
        CompanyPhone = c.CompanyPhone,
        CompanyEmail = c.CompanyEmail,
        TaxNumber = c.TaxNumber,
        Currency = c.Currency,
        BrandPrimaryColor = c.BrandPrimaryColor,
        BrandSecondaryColor = c.BrandSecondaryColor,
        RequireCustomerApproval = c.RequireCustomerApproval,
        RequireQuotationApproval = c.RequireQuotationApproval,
        DefaultPaymentTermsDays = c.DefaultPaymentTermsDays,
        DefaultCreditLimit = c.DefaultCreditLimit
    };
}
