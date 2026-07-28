namespace DistributionSystem.Application.DTOs.RepPayments;

public class PaymentReportEventDto
{
    public string EventType { get; set; } = string.Empty; // paymentReportCreated | paymentReportStatusChanged | paymentReportTrashed | paymentReportRestored
    public Guid ReportId { get; set; }
    public Guid SalesRepUserId { get; set; }
    public Guid? CoordinatorUserId { get; set; }
    public string? OldStatus { get; set; }
    public string? NewStatus { get; set; }
    public string? ActionUrl { get; set; }
    public DateTime OccurredAt { get; set; } = DateTime.UtcNow;
}
