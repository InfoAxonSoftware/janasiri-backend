namespace DistributionSystem.Domain.Entities;

// Guest orders keep their existing primary RepId for compatibility.
// This table contains additional assigned sales reps only.
public sealed class GuestOrderAdditionalRep
{
    public Guid QuickRequestId { get; set; }
    public QuickRequest QuickRequest { get; set; } = null!;
    public Guid RepId { get; set; }
    public SalesRepProfile Rep { get; set; } = null!;
}
