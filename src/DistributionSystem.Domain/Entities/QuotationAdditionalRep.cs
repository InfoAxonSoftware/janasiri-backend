namespace DistributionSystem.Domain.Entities;
public sealed class QuotationAdditionalRep
{
    public Guid QuotationId { get; set; }
    public Quotation Quotation { get; set; } = null!;
    public Guid RepId { get; set; }
    public SalesRepProfile Rep { get; set; } = null!;
}