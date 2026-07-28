using DistributionSystem.Application.DTOs.Customer;
using FluentValidation;

namespace DistributionSystem.Application.Validators;

public class CreateCustomerValidator : AbstractValidator<CreateCustomerRequest>
{
    public CreateCustomerValidator()
    {
        RuleFor(x => x.Username).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Email).EmailAddress().When(x => !string.IsNullOrWhiteSpace(x.Email));
        RuleFor(x => x.Password).NotEmpty().MinimumLength(6);
        // PhoneNumber is optional for admin-created / bulk-imported customers
        RuleFor(x => x.PhoneNumber).MaximumLength(50).When(x => !string.IsNullOrWhiteSpace(x.PhoneNumber));
        RuleFor(x => x.ShopName).NotEmpty().MaximumLength(300);
    }
}
