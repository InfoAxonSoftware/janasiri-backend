namespace DistributionSystem.Domain.Entities;

/// <summary>Holds one uploaded outstanding/ageing report per region.</summary>
public class OutstandingReport
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>FK to Region</summary>
    public Guid RegionId { get; set; }

    /// <summary>Snapshot of region name at upload time</summary>
    public string RegionName { get; set; } = string.Empty;

    /// <summary>"Date As Of" from the report header (e.g. 24-06-2026)</summary>
    public DateTime? ReportDate { get; set; }

    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;
    public string? UploadedBy { get; set; }

    public Region Region { get; set; } = null!;
    public ICollection<OutstandingEntry> Entries { get; set; } = new List<OutstandingEntry>();
}
