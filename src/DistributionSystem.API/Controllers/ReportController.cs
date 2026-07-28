using System.Security.Claims;
using DistributionSystem.Application.DTOs.Common;
using DistributionSystem.Application.DTOs.Report;
using DistributionSystem.Application.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DistributionSystem.API.Controllers;

/// <summary>
/// Reports and analytics endpoints (Admin)
/// </summary>
[ApiController]
[Route("api/admin/reports")]
[Authorize(Roles = "Admin,SuperAdmin")]
[Produces("application/json")]
public class ReportController : ControllerBase
{
    private readonly IReportService _reportService;

    public ReportController(IReportService reportService)
    {
        _reportService = reportService;
    }

    /// <summary>Get admin dashboard KPIs</summary>
    [HttpGet("dashboard")]
    public async Task<IActionResult> GetDashboard(CancellationToken ct)
    {
        var result = await _reportService.GetDashboardAsync(ct);
        return Ok(ApiResponse<DashboardDto>.SuccessResponse(result));
    }

    /// <summary>Get sales report with date range</summary>
    [HttpGet("sales")]
    public async Task<IActionResult> GetSalesReport([FromQuery] DateTime from, [FromQuery] DateTime to,
        [FromQuery] string groupBy = "day", CancellationToken ct = default)
    {
        var filter = new ReportFilterRequest { FromDate = from, ToDate = to };
        var result = await _reportService.GetSalesReportAsync(filter, ct);
        return Ok(ApiResponse<SalesReportDto>.SuccessResponse(result));
    }

    /// <summary>Get best-selling products</summary>
    [HttpGet("products/best-selling")]
    public async Task<IActionResult> GetBestSellingProducts([FromQuery] DateTime? from, [FromQuery] DateTime? to,
        [FromQuery] int top = 10, CancellationToken ct = default)
    {
        var filter = new ReportFilterRequest
        {
            FromDate = from ?? DateTime.UtcNow.AddMonths(-1),
            ToDate = to ?? DateTime.UtcNow,
            Top = top
        };
        var result = await _reportService.GetBestSellingProductsAsync(filter, ct);
        return Ok(ApiResponse<List<TopProductDto>>.SuccessResponse(result));
    }

    /// <summary>Get slow-moving products</summary>
    [HttpGet("products/slow-moving")]
    public async Task<IActionResult> GetSlowMovingProducts([FromQuery] DateTime? from, [FromQuery] DateTime? to,
        [FromQuery] int top = 10, CancellationToken ct = default)
    {
        var filter = new ReportFilterRequest
        {
            FromDate = from ?? DateTime.UtcNow.AddMonths(-3),
            ToDate = to ?? DateTime.UtcNow,
            Top = top
        };
        var result = await _reportService.GetSlowMovingProductsAsync(filter, ct);
        return Ok(ApiResponse<List<TopProductDto>>.SuccessResponse(result));
    }

    /// <summary>Get customer activity report</summary>
    [HttpGet("customers/activity")]
    public async Task<IActionResult> GetCustomerActivity([FromQuery] DateTime? from, [FromQuery] DateTime? to, CancellationToken ct = default)
    {
        var filter = new ReportFilterRequest
        {
            FromDate = from ?? DateTime.UtcNow.AddMonths(-1),
            ToDate = to ?? DateTime.UtcNow
        };
        var result = await _reportService.GetCustomerActivityAsync(filter, ct);
        return Ok(ApiResponse<List<CustomerActivityDto>>.SuccessResponse(result));
    }

    /// <summary>Get lost/inactive customers</summary>
    [HttpGet("customers/lost")]
    public async Task<IActionResult> GetLostCustomers([FromQuery] int inactiveDays = 30, CancellationToken ct = default)
    {
        var result = await _reportService.GetLostCustomersAsync(inactiveDays, ct);
        return Ok(ApiResponse<List<CustomerActivityDto>>.SuccessResponse(result));
    }

    /// <summary>Get outstanding payments summary</summary>
    [HttpGet("payments/outstanding")]
    public async Task<IActionResult> GetOutstandingPayments(CancellationToken ct)
    {
        var result = await _reportService.GetOutstandingPaymentsAsync(ct);
        return Ok(ApiResponse<List<PaymentReportDto>>.SuccessResponse(result));
    }
}

