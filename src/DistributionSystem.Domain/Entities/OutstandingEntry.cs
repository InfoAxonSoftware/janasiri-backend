namespace DistributionSystem.Domain.Entities;

/// <summary>One row from the uploaded outstanding Excel file.</summary>
public class OutstandingEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OutstandingReportId { get; set; }

    public string CustomerName { get; set; } = string.Empty;

    /// <summary>Invoice / Credit Note / Receive Payment — null on total rows</summary>
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

    /// <summary>True for the per-customer summary/total row</summary>
    public bool IsTotal { get; set; }

    /// <summary>Preserves the original row order from the Excel file</summary>
    public int SortOrder { get; set; }

    public OutstandingReport Report { get; set; } = null!;
}
