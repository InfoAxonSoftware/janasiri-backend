using DistributionSystem.Application.DTOs.TargetReports;

namespace DistributionSystem.Application.Services.Interfaces;

public interface ITargetReportService
{
    // Admin
    Task<TargetReportSummaryDto> UploadReportAsync(Guid targetId, UploadTargetReportRequest request, Guid uploadedByUserId, string uploadedBy, CancellationToken ct = default);
    Task<List<TargetReportSummaryDto>> GetHistoryAsync(Guid targetId, CancellationToken ct = default);
    Task<TargetReportDetailDto> GetReportByIdAsync(Guid reportId, CancellationToken ct = default);
    Task<TargetReportDetailDto> GetCurrentReportAsync(Guid targetId, CancellationToken ct = default);

    // Rep (ownership-checked)
    Task<TargetReportDetailDto> GetRepCurrentReportAsync(Guid targetId, Guid repUserId, CancellationToken ct = default);
    Task<List<TargetReportSummaryDto>> GetRepHistoryAsync(Guid targetId, Guid repUserId, CancellationToken ct = default);
    Task<TargetReportDetailDto> GetRepReportByIdAsync(Guid reportId, Guid repUserId, CancellationToken ct = default);
}
