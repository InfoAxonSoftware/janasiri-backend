using DistributionSystem.Application.DTOs.Product;
using FluentValidation;

namespace DistributionSystem.Application.Validators;

public class CreateProductValidator : AbstractValidator<CreateProductRequest>
{
    public CreateProductValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(300);
        RuleFor(x => x.SKU).NotEmpty().MaximumLength(50);
        // category is optional for import sheets; if provided must be non-empty
        RuleFor(x => x.CategoryId).NotEmpty().When(x => x.CategoryId.HasValue);
        RuleFor(x => x.SellingPrice).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Quantity).GreaterThanOrEqualTo(0);

        // optional import-specific fields
        RuleFor(x => x.DiscountPercent).InclusiveBetween(0, 100).When(x => x.DiscountPercent.HasValue);
        RuleFor(x => x.DiscountAmount).GreaterThanOrEqualTo(0).When(x => x.DiscountAmount.HasValue);
        RuleFor(x => x.TaxAmount).GreaterThanOrEqualTo(0).When(x => x.TaxAmount.HasValue);
        RuleFor(x => x.TotalAmount).GreaterThanOrEqualTo(0).When(x => x.TotalAmount.HasValue);
    }
}

public class UpdateProductValidator : AbstractValidator<UpdateProductRequest>
{
    public UpdateProductValidator()
    {
        RuleFor(x => x.Name).MaximumLength(300).When(x => x.Name != null);
        RuleFor(x => x.SellingPrice).GreaterThanOrEqualTo(0).When(x => x.SellingPrice.HasValue);
        RuleFor(x => x.Quantity).GreaterThanOrEqualTo(0).When(x => x.Quantity.HasValue);
    }
}
