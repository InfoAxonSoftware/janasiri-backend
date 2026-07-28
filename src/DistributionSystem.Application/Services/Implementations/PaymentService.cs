using DistributionSystem.Application.DTOs.Common;
using DistributionSystem.Application.DTOs.Payment;
using DistributionSystem.Application.Exceptions;
using DistributionSystem.Application.Services.Interfaces;
using DistributionSystem.Domain.Entities;
using DistributionSystem.Domain.Enums;
using DistributionSystem.Infrastructure.Data.Repositories.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace DistributionSystem.Application.Services.Implementations;

public class PaymentService : IPaymentService
{
    private readonly IUnitOfWork _unitOfWork;

    public PaymentService(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<PaymentDto> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var payment = await _unitOfWork.Repository<Payment>().Query()
            .Include(p => p.Customer).ThenInclude(c => c.User)
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken)
            ?? throw new NotFoundException("Payment", id);
        return MapToDto(payment);
    }

    public async Task<PaymentDto> CreateAsync(CreatePaymentRequest request, Guid? collectedByRepId = null, CancellationToken cancellationToken = default)
    {
        var customer = await _unitOfWork.Repository<CustomerProfile>().GetByIdAsync(request.CustomerId, cancellationToken)
            ?? throw new NotFoundException("Customer", request.CustomerId);

        var payment = new Payment
        {
            CustomerId = request.CustomerId,
            OrderId = request.OrderId,
            Amount = request.Amount,
            PaymentMethod = request.PaymentMethod,
            ReferenceNumber = request.ReferenceNumber,
            ChequeNumber = request.ChequeNumber,
            BankName = request.BankName,
            ChequeDate = request.ChequeDate,
            CollectedByRepId = collectedByRepId,
            Status = PaymentStatus.Pending,
            Notes = request.Notes
        };

        await _unitOfWork.Repository<Payment>().AddAsync(payment, cancellationToken);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        payment.Customer = customer;
        return MapToDto(payment);
    }

    public async Task<PagedResult<PaymentDto>> GetAllAsync(int page, int pageSize, Guid? customerId = null, CancellationToken cancellationToken = default)
    {
        var query = _unitOfWork.Repository<Payment>().Query()
            .Include(p => p.Customer).ThenInclude(c => c.User)
            .AsQueryable();

        if (customerId.HasValue)
            query = query.Where(p => p.CustomerId == customerId.Value);

        var total = await query.CountAsync(cancellationToken);
        var items = await query.OrderByDescending(p => p.PaymentDate)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<PaymentDto>
        {
            Items = items.Select(MapToDto),
            TotalCount = total,
            Page = page,
            PageSize = pageSize
        };
    }

    public async Task<PagedResult<PaymentDto>> GetByRepAsync(Guid repUserId, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var rep = await _unitOfWork.Repository<SalesRepProfile>().Query()
            .FirstOrDefaultAsync(r => r.UserId == repUserId, cancellationToken)
            ?? throw new NotFoundException("SalesRep", repUserId);

        var query = _unitOfWork.Repository<Payment>().Query()
            .Include(p => p.Customer).ThenInclude(c => c.User)
            .Where(p => p.CollectedByRepId == rep.Id);

        var total = await query.CountAsync(cancellationToken);
        var items = await query.OrderByDescending(p => p.PaymentDate)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<PaymentDto>
        {
            Items = items.Select(MapToDto),
            TotalCount = total,
            Page = page,
            PageSize = pageSize
        };
    }

    public async Task<PagedResult<PaymentDto>> GetByCustomerUserIdAsync(Guid customerUserId, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var customer = await _unitOfWork.Repository<CustomerProfile>().Query()
            .FirstOrDefaultAsync(c => c.UserId == customerUserId, cancellationToken)
            ?? throw new NotFoundException("Customer", customerUserId);

        var query = _unitOfWork.Repository<Payment>().Query()
            .Include(p => p.Customer).ThenInclude(c => c.User)
            .Where(p => p.CustomerId == customer.Id);

        var total = await query.CountAsync(cancellationToken);
        var items = await query.OrderByDescending(p => p.PaymentDate)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<PaymentDto>
        {
            Items = items.Select(MapToDto),
            TotalCount = total,
            Page = page,
            PageSize = pageSize
        };
    }

    public async Task<PaymentDto> VerifyAsync(Guid paymentId, CancellationToken cancellationToken = default)
    {
        var payment = await _unitOfWork.Repository<Payment>().Query()
            .Include(p => p.Customer).ThenInclude(c => c.User)
            .FirstOrDefaultAsync(p => p.Id == paymentId, cancellationToken)
            ?? throw new NotFoundException("Payment", paymentId);

        payment.Status = PaymentStatus.Verified;
        _unitOfWork.Repository<Payment>().Update(payment);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return MapToDto(payment);
    }

    public async Task AllocatePaymentAsync(AllocatePaymentRequest request, CancellationToken cancellationToken = default)
    {
        var payment = await _unitOfWork.Repository<Payment>().GetByIdAsync(request.PaymentId, cancellationToken)
            ?? throw new NotFoundException("Payment", request.PaymentId);

        var totalAllocated = request.Allocations.Sum(a => a.Amount);
        if (totalAllocated > payment.Amount)
            throw new BusinessException("Total allocation exceeds payment amount");

        foreach (var alloc in request.Allocations)
        {
            await _unitOfWork.Repository<PaymentAllocation>().AddAsync(new PaymentAllocation
            {
                PaymentId = request.PaymentId,
                OrderId = alloc.OrderId,
                AllocatedAmount = alloc.Amount
            }, cancellationToken);
        }

        payment.Status = PaymentStatus.Allocated;
        _unitOfWork.Repository<Payment>().Update(payment);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task<CustomerLedgerDto> GetCustomerLedgerAsync(Guid customerId, CancellationToken cancellationToken = default)
    {
        var customer = await _unitOfWork.Repository<CustomerProfile>().GetByIdAsync(customerId, cancellationToken)
            ?? throw new NotFoundException("Customer", customerId);

        return await BuildLedger(customer, cancellationToken);
    }

    public async Task<CustomerLedgerDto> GetCustomerLedgerByUserIdAsync(Guid customerUserId, CancellationToken cancellationToken = default)
    {
        var customer = await _unitOfWork.Repository<CustomerProfile>().Query()
            .FirstOrDefaultAsync(c => c.UserId == customerUserId, cancellationToken)
            ?? throw new NotFoundException("Customer", customerUserId);

        return await BuildLedger(customer, cancellationToken);
    }

    private async Task<CustomerLedgerDto> BuildLedger(CustomerProfile customer, CancellationToken cancellationToken)
    {
        var orders = await _unitOfWork.Repository<Order>().Query()
            .Where(o => o.CustomerId == customer.Id && o.Status != OrderStatus.Cancelled && o.Status != OrderStatus.Rejected)
            .OrderBy(o => o.OrderDate)
            .ToListAsync(cancellationToken);

        var payments = await _unitOfWork.Repository<Payment>().Query()
            .Where(p => p.CustomerId == customer.Id)
            .OrderBy(p => p.PaymentDate)
            .ToListAsync(cancellationToken);

        var entries = new List<LedgerEntryDto>();
        decimal runningBalance = 0;

        var allEntries = orders.Select(o => new { Date = o.OrderDate, Type = "Invoice", Reference = o.OrderNumber, Debit = o.TotalAmount, Credit = 0m })
            .Concat(payments.Select(p => new { Date = p.PaymentDate, Type = "Payment", Reference = p.ReferenceNumber ?? p.Id.ToString()[..8], Debit = 0m, Credit = p.Amount }))
            .OrderBy(e => e.Date);

        foreach (var entry in allEntries)
        {
            runningBalance += entry.Debit - entry.Credit;
            entries.Add(new LedgerEntryDto
            {
                Date = entry.Date,
                Type = entry.Type,
                Reference = entry.Reference,
                Debit = entry.Debit,
                Credit = entry.Credit,
                Balance = runningBalance
            });
        }

        var totalPaid = payments.Sum(p => p.Amount);
        var totalInvoiced = orders.Sum(o => o.TotalAmount);

        return new CustomerLedgerDto
        {
            TotalOutstanding = totalInvoiced - totalPaid,
            TotalPaid = totalPaid,
            Entries = entries
        };
    }

    private static PaymentDto MapToDto(Payment p) => new()
    {
        Id = p.Id,
        CustomerId = p.CustomerId,
        CustomerName = p.Customer?.ShopName ?? "",
        OrderId = p.OrderId,
        PaymentDate = p.PaymentDate,
        Amount = p.Amount,
        PaymentMethod = p.PaymentMethod.ToString(),
        ReferenceNumber = p.ReferenceNumber,
        ChequeNumber = p.ChequeNumber,
        BankName = p.BankName,
        Status = p.Status.ToString(),
        Notes = p.Notes,
        CreatedAt = p.CreatedAt
    };
}
