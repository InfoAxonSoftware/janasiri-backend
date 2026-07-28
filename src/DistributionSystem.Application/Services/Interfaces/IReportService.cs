using DistributionSystem.Application.DTOs.Report;

namespace DistributionSystem.Application.Services.Interfaces;

public interface IReportService
{
    Task<DashboardDto> GetDashboardAsync(CancellationToken cancellationToken = default);
    Task<SalesReportDto> GetSalesReportAsync(ReportFilterRequest filter, CancellationToken cancellationToken = default);
    Task<List<TopProductDto>> GetBestSellingProductsAsync(ReportFilterRequest filter, CancellationToken cancellationToken = default);
    Task<List<TopProductDto>> GetSlowMovingProductsAsync(ReportFilterRequest filter, CancellationToken cancellationToken = default);
    Task<List<CustomerActivityDto>> GetCustomerActivityAsync(ReportFilterRequest filter, CancellationToken cancellationToken = default);
    Task<List<CustomerActivityDto>> GetLostCustomersAsync(int inactiveDays = 30, CancellationToken cancellationToken = default);
    Task<List<PaymentReportDto>> GetOutstandingPaymentsAsync(CancellationToken cancellationToken = default);
}
