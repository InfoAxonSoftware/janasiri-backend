using DistributionSystem.Domain.Enums;

namespace DistributionSystem.Application.DTOs.Payment;

public class PaymentDto
{
    public Guid Id { get; set; }
    public Guid CustomerId { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public Guid? OrderId { get; set; }
    public DateTime PaymentDate { get; set; }
    public decimal Amount { get; set; }
    public string PaymentMethod { get; set; } = string.Empty;
    public string? ReferenceNumber { get; set; }
    public string? ChequeNumber { get; set; }
    public string? BankName { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class CreatePaymentRequest
{
    public Guid CustomerId { get; set; }
    public Guid? OrderId { get; set; }
    public decimal Amount { get; set; }
    public PaymentMethod PaymentMethod { get; set; }
    public string? ReferenceNumber { get; set; }
    public string? ChequeNumber { get; set; }
    public string? BankName { get; set; }
    public DateTime? ChequeDate { get; set; }
    public string? Notes { get; set; }
}

public class AllocatePaymentRequest
{
    public Guid PaymentId { get; set; }
    public List<AllocationItem> Allocations { get; set; } = [];
}

public class AllocationItem
{
    public Guid OrderId { get; set; }
    public decimal Amount { get; set; }
}

public class LedgerDto
{
    public decimal TotalOutstanding { get; set; }
    public decimal TotalPaid { get; set; }
    public List<LedgerEntryDto> Entries { get; set; } = [];
}

// Alias for controller usage
public class CustomerLedgerDto : LedgerDto { }

public class LedgerEntryDto
{
    public DateTime Date { get; set; }
    public string Type { get; set; } = string.Empty; // Invoice, Payment
    public string Reference { get; set; } = string.Empty;
    public decimal Debit { get; set; }
    public decimal Credit { get; set; }
    public decimal Balance { get; set; }
}
