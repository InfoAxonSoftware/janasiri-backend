namespace DistributionSystem.Application.DTOs.Outstanding;

// ── Response DTOs ────────────────────────────────────────────────────────────

public class OutstandingReportDto
{
    public Guid Id { get; set; }
    public Guid RegionId { get; set; }
    public string RegionName { get; set; } = string.Empty;
    public DateTime? ReportDate { get; set; }
    public DateTime UploadedAt { get; set; }
    public string? UploadedBy { get; set; }
    public int CustomerCount { get; set; }
    public int EntryCount { get; set; }
}

public class OutstandingEntryDto
{
    public Guid Id { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string? TxnType { get; set; }
    public string? RefNo { get; set; }
    public DateTime? TxnDate { get; set; }
    public int? AgeDays { get; set; }
    public decimal Current { get; set; }
    public decimal Bucket1_15 { get; set; }
    public decimal Bucket16_30 { get; set; }
    public decimal Bucket31_45 { get; set; }
    public decimal Above45 { get; set; }
    public decimal Balance { get; set; }
    public bool IsTotal { get; set; }
    public int SortOrder { get; set; }
}

public class OutstandingReportDetailDto
{
    public Guid Id { get; set; }
    public Guid RegionId { get; set; }
    public string RegionName { get; set; } = string.Empty;
    public DateTime? ReportDate { get; set; }
    public DateTime UploadedAt { get; set; }
    public string? UploadedBy { get; set; }
    public List<OutstandingEntryDto> Entries { get; set; } = [];
}

// ── Request DTOs ─────────────────────────────────────────────────────────────

public class UploadOutstandingRequest
{
    public Guid RegionId { get; set; }
    public DateTime? ReportDate { get; set; }
}

// ── Chunk-upload DTOs ─────────────────────────────────────────────────────────

/// <summary>Start a new chunk upload session — creates / replaces the report header.</summary>
public class StartOutstandingUploadRequest
{
    public Guid RegionId { get; set; }
    public DateTime? ReportDate { get; set; }
    public int TotalEntries { get; set; }
}

public class StartOutstandingUploadResponse
{
    public Guid ReportId { get; set; }
}

/// <summary>One row sent from the client after client-side Excel parsing.</summary>
public class OutstandingEntryRequest
{
    public string CustomerName { get; set; } = string.Empty;
    public string? TxnType { get; set; }
    public string? RefNo { get; set; }
    public DateTime? TxnDate { get; set; }
    public int? AgeDays { get; set; }
    public decimal Current { get; set; }
    public decimal Bucket1_15 { get; set; }
    public decimal Bucket16_30 { get; set; }
    public decimal Bucket31_45 { get; set; }
    public decimal Above45 { get; set; }
    public decimal Balance { get; set; }
    public bool IsTotal { get; set; }
    public int SortOrder { get; set; }
}

public class AppendEntriesRequest
{
    public List<OutstandingEntryRequest> Entries { get; set; } = [];
}
