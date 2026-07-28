using DistributionSystem.Application.DTOs.Outstanding;
using Microsoft.AspNetCore.Http;

namespace DistributionSystem.Application.Services.Interfaces;

public interface IOutstandingReportService
{
    // Admin — file upload (legacy / kept for compatibility)
    Task<OutstandingReportDto> UploadReportAsync(Guid regionId, DateTime? reportDate, IFormFile file, string uploadedBy, CancellationToken ct = default);
    Task<List<OutstandingReportDto>> GetAllReportsAsync(CancellationToken ct = default);
    Task<OutstandingReportDetailDto> GetReportByRegionAsync(Guid regionId, CancellationToken ct = default);
    Task DeleteReportAsync(Guid regionId, CancellationToken ct = default);

    // Admin — chunk-upload (client-side Excel parsing)
    Task<StartOutstandingUploadResponse> StartUploadAsync(Guid regionId, DateTime? reportDate, string uploadedBy, CancellationToken ct = default);
    Task AppendEntriesAsync(Guid reportId, List<OutstandingEntryRequest> entries, CancellationToken ct = default);

    // Rep — returns reports only for regions the rep is assigned to
    Task<List<OutstandingReportDetailDto>> GetRepReportsAsync(Guid repUserId, CancellationToken ct = default);
}
