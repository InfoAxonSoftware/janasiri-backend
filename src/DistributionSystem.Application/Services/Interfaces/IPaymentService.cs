using DistributionSystem.Application.DTOs.Common;
using DistributionSystem.Application.DTOs.Payment;

namespace DistributionSystem.Application.Services.Interfaces;

public interface IPaymentService
{
    Task<PaymentDto> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<PaymentDto> CreateAsync(CreatePaymentRequest request, Guid? collectedByRepId = null, CancellationToken cancellationToken = default);
    Task<PagedResult<PaymentDto>> GetAllAsync(int page, int pageSize, Guid? customerId = null, CancellationToken cancellationToken = default);
    Task<PagedResult<PaymentDto>> GetByRepAsync(Guid repUserId, int page, int pageSize, CancellationToken cancellationToken = default);
    Task<PagedResult<PaymentDto>> GetByCustomerUserIdAsync(Guid customerUserId, int page, int pageSize, CancellationToken cancellationToken = default);
    Task<PaymentDto> VerifyAsync(Guid paymentId, CancellationToken cancellationToken = default);
    Task AllocatePaymentAsync(AllocatePaymentRequest request, CancellationToken cancellationToken = default);
    Task<CustomerLedgerDto> GetCustomerLedgerAsync(Guid customerId, CancellationToken cancellationToken = default);
    Task<CustomerLedgerDto> GetCustomerLedgerByUserIdAsync(Guid customerUserId, CancellationToken cancellationToken = default);
}
