using DistributionSystem.Application.DTOs.Stock;
using Microsoft.AspNetCore.Http;

namespace DistributionSystem.Application.Services.Interfaces;

public interface IStockReportService
{
    Task<StockReportSummaryDto> UploadReportAsync(Guid regionId, IFormFile file, string uploadedBy, CancellationToken ct = default);

    Task<List<StockReportSummaryDto>> GetAllReportsAsync(CancellationToken ct = default);

    Task<StockReportDetailDto> GetReportByRegionAsync(Guid regionId, CancellationToken ct = default);

    Task DeleteReportAsync(Guid regionId, CancellationToken ct = default);

    /// <summary>Reports for every region assigned to the given sales rep.</summary>
    Task<List<StockReportSummaryDto>> GetRepReportsAsync(Guid repUserId, CancellationToken ct = default);

    /// <summary>Single region's report, but only if that region is assigned to the given sales rep — throws ForbiddenException otherwise.</summary>
    Task<StockReportDetailDto> GetRepReportByRegionAsync(Guid repUserId, Guid regionId, CancellationToken ct = default);
}
