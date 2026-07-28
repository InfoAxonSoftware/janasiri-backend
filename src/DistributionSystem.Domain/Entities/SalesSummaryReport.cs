namespace DistributionSystem.Domain.Entities;

public class SalesSummaryReport
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RegionId { get; set; }
    public string RegionName { get; set; } = string.Empty; // snapshot of region name at upload time
    public DateTime? PeriodFrom { get; set; }
    public DateTime? PeriodTo { get; set; }
    public string? OriginalFileName { get; set; }
    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;
    public string? UploadedBy { get; set; }

    public Region Region { get; set; } = null!;
    public ICollection<SalesSummaryEntry> Entries { get; set; } = new List<SalesSummaryEntry>();
}
