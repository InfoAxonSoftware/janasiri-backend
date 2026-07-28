using DistributionSystem.Application.DTOs.SalesSummary;

namespace DistributionSystem.Application.Services.Interfaces;

public interface ISalesSummaryReportService
{
    // Admin
    Task<SalesSummaryReportDto> UploadReportAsync(UploadSalesSummaryRequest request, string uploadedBy, CancellationToken ct = default);
    Task<List<SalesSummaryReportDto>> GetAllReportsAsync(CancellationToken ct = default);
    Task<SalesSummaryReportDetailDto> GetReportByIdAsync(Guid reportId, CancellationToken ct = default);
    Task DeleteReportAsync(Guid reportId, CancellationToken ct = default);

    // Rep
    Task<List<SalesSummaryReportDetailDto>> GetRepReportsAsync(Guid repUserId, CancellationToken ct = default);
    Task<SalesSummaryReportDetailDto> GetRepReportByIdAsync(Guid reportId, Guid repUserId, CancellationToken ct = default);
}
