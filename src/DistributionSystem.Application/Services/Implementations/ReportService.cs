using DistributionSystem.Application.DTOs.Report;
using DistributionSystem.Application.Services.Interfaces;
using DistributionSystem.Domain.Entities;
using DistributionSystem.Domain.Enums;
using DistributionSystem.Infrastructure.Data.Repositories.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace DistributionSystem.Application.Services.Implementations;

public class ReportService : IReportService
{
    private readonly IUnitOfWork _unitOfWork;

    public ReportService(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<DashboardDto> GetDashboardAsync(CancellationToken cancellationToken = default)
    {
        var today = DateTime.UtcNow.Date;
        var monthStart = new DateTime(today.Year, today.Month, 1);

        // fetch all non-cancelled orders once
        var allOrders = await _unitOfWork.Repository<Order>().Query()
            .Where(o => o.Status != OrderStatus.Cancelled)
            .ToListAsync(cancellationToken);

        var ordersToday = allOrders
            .Where(o => o.OrderDate.Date == today)
            .ToList();

        var ordersMonth = allOrders
            .Where(o => o.OrderDate >= monthStart)
            .ToList();

        var pendingOrders = allOrders.Count(o => o.Status == OrderStatus.Pending);
        var totalProducts = await _unitOfWork.Repository<Product>().CountAsync(cancellationToken: cancellationToken);

        // inventory tracking removed; low stock not supported anymore
        var lowStockProducts = 0;

        var totalCustomers = await _unitOfWork.Repository<CustomerProfile>().CountAsync(cancellationToken: cancellationToken);
        var activeReps = await _unitOfWork.Repository<SalesRepProfile>().Query()
            .CountAsync(r => r.User.IsActive, cancellationToken);

        var totalOutstanding = 0m; // Balance tracking removed

        // Sales trend for last 7 days
        var salesTrend = new List<SalesTrendDto>();
        for (int i = 6; i >= 0; i--)
        {
            var date = today.AddDays(-i);
            var dayOrders = ordersMonth.Where(o => o.OrderDate.Date == date).ToList();
            salesTrend.Add(new SalesTrendDto
            {
                Period = date.ToString("MMM dd"),
                Amount = dayOrders.Sum(o => o.TotalAmount),
                OrderCount = dayOrders.Count
            });
        }

        // Top products
        var topProducts = await _unitOfWork.Repository<OrderItem>().Query()
            .Include(oi => oi.Product).Include(oi => oi.Order)
            .Where(oi => oi.Order.OrderDate >= monthStart && oi.Order.Status != OrderStatus.Cancelled && oi.ProductId.HasValue)
            .GroupBy(oi => new { ProductId = oi.ProductId!.Value, oi.Product!.Name })
            .Select(g => new TopProductDto
            {
                ProductId = g.Key.ProductId,
                ProductName = g.Key.Name,
                QuantitySold = g.Sum(oi => oi.Quantity),
                Revenue = g.Sum(oi => oi.LineTotal)
            })
            .OrderByDescending(p => p.Revenue)
            .Take(5)
            .ToListAsync(cancellationToken);

        return new DashboardDto
        {
            TotalSalesToday = ordersToday.Sum(o => o.TotalAmount),
            TotalSalesMonth = ordersMonth.Sum(o => o.TotalAmount),
            TotalOrders = allOrders.Count,            // include all active orders regardless of month
            PendingOrders = pendingOrders,
            TotalProducts = totalProducts,
            LowStockProducts = lowStockProducts,
            TotalCustomers = totalCustomers,
            ActiveReps = activeReps,
            TotalOutstanding = totalOutstanding,
            SalesTrend = salesTrend,
            TopProducts = topProducts
        };
    }

    public async Task<SalesReportDto> GetSalesReportAsync(ReportFilterRequest filter, CancellationToken cancellationToken = default)
    {
        var from = filter.FromDate ?? DateTime.UtcNow.AddDays(-30);
        var to = filter.ToDate ?? DateTime.UtcNow;

        var orders = await _unitOfWork.Repository<Order>().Query()
            .Where(o => o.OrderDate >= from && o.OrderDate <= to && o.Status != OrderStatus.Cancelled)
            .ToListAsync(cancellationToken);

        var dailyBreakdown = orders.GroupBy(o => o.OrderDate.Date)
            .Select(g => new SalesTrendDto
            {
                Period = g.Key.ToString("yyyy-MM-dd"),
                Amount = g.Sum(o => o.TotalAmount),
                OrderCount = g.Count()
            })
            .OrderBy(s => s.Period)
            .ToList();

        return new SalesReportDto
        {
            FromDate = from,
            ToDate = to,
            TotalSales = orders.Sum(o => o.TotalAmount),
            TotalOrders = orders.Count,
            AverageOrderValue = orders.Count > 0 ? orders.Average(o => o.TotalAmount) : 0,
            DailyBreakdown = dailyBreakdown
        };
    }

    public async Task<List<TopProductDto>> GetBestSellingProductsAsync(ReportFilterRequest filter, CancellationToken cancellationToken = default)
    {
        var from = filter.FromDate ?? DateTime.UtcNow.AddDays(-30);
        var to = filter.ToDate ?? DateTime.UtcNow;

        return await _unitOfWork.Repository<OrderItem>().Query()
            .Include(oi => oi.Product).Include(oi => oi.Order)
            .Where(oi => oi.Order.OrderDate >= from && oi.Order.OrderDate <= to && oi.Order.Status != OrderStatus.Cancelled && oi.ProductId.HasValue)
            .GroupBy(oi => new { ProductId = oi.ProductId!.Value, oi.Product!.Name })
            .Select(g => new TopProductDto
            {
                ProductId = g.Key.ProductId,
                ProductName = g.Key.Name,
                QuantitySold = g.Sum(oi => oi.Quantity),
                Revenue = g.Sum(oi => oi.LineTotal)
            })
            .OrderByDescending(p => p.QuantitySold)
            .Take(filter.Top)
            .ToListAsync(cancellationToken);
    }

    public async Task<List<TopProductDto>> GetSlowMovingProductsAsync(ReportFilterRequest filter, CancellationToken cancellationToken = default)
    {
        var from = filter.FromDate ?? DateTime.UtcNow.AddDays(-30);
        var to = filter.ToDate ?? DateTime.UtcNow;

        var soldProductIds = await _unitOfWork.Repository<OrderItem>().Query()
            .Include(oi => oi.Order)
            .Where(oi => oi.Order.OrderDate >= from && oi.Order.OrderDate <= to)
            .Select(oi => oi.ProductId)
            .Distinct()
            .ToListAsync(cancellationToken);

        var slowMoving = await _unitOfWork.Repository<Product>().Query()
            .Where(p => !soldProductIds.Contains(p.Id))
            .Select(p => new TopProductDto
            {
                ProductId = p.Id,
                ProductName = p.Name,
                QuantitySold = 0,
                Revenue = 0
            })
            .Take(filter.Top)
            .ToListAsync(cancellationToken);

        return slowMoving;
    }

    public async Task<List<CustomerActivityDto>> GetCustomerActivityAsync(ReportFilterRequest filter, CancellationToken cancellationToken = default)
    {
        var from = filter.FromDate ?? DateTime.UtcNow.AddDays(-30);
        var to = filter.ToDate ?? DateTime.UtcNow;

        var customers = await _unitOfWork.Repository<CustomerProfile>().Query()
            .Include(c => c.Orders)
            .ToListAsync(cancellationToken);

        return customers.Select(c =>
        {
            var periodOrders = c.Orders.Where(o => o.OrderDate >= from && o.OrderDate <= to && o.Status != OrderStatus.Cancelled);
            var lastOrder = c.Orders.Where(o => o.Status != OrderStatus.Cancelled).MaxBy(o => o.OrderDate);
            return new CustomerActivityDto
            {
                CustomerId = c.Id,
                ShopName = c.ShopName,
                OrderCount = periodOrders.Count(),
                TotalSpent = periodOrders.Sum(o => o.TotalAmount),
                LastOrderDate = lastOrder?.OrderDate,
                DaysSinceLastOrder = lastOrder != null ? (int)(DateTime.UtcNow - lastOrder.OrderDate).TotalDays : 9999
            };
        })
        .OrderByDescending(c => c.TotalSpent)
        .ToList();
    }

    public async Task<List<CustomerActivityDto>> GetLostCustomersAsync(int inactiveDays = 30, CancellationToken cancellationToken = default)
    {
        var cutoffDate = DateTime.UtcNow.AddDays(-inactiveDays);

        var customers = await _unitOfWork.Repository<CustomerProfile>().Query()
            .Include(c => c.Orders)
            .ToListAsync(cancellationToken);

        return customers
            .Where(c =>
            {
                var lastOrder = c.Orders.Where(o => o.Status != OrderStatus.Cancelled).MaxBy(o => o.OrderDate);
                return lastOrder == null || lastOrder.OrderDate < cutoffDate;
            })
            .Select(c =>
            {
                var lastOrder = c.Orders.Where(o => o.Status != OrderStatus.Cancelled).MaxBy(o => o.OrderDate);
                return new CustomerActivityDto
                {
                    CustomerId = c.Id,
                    ShopName = c.ShopName,
                    OrderCount = c.Orders.Count(o => o.Status != OrderStatus.Cancelled),
                    TotalSpent = c.Orders.Where(o => o.Status != OrderStatus.Cancelled).Sum(o => o.TotalAmount),
                    LastOrderDate = lastOrder?.OrderDate,
                    DaysSinceLastOrder = lastOrder != null ? (int)(DateTime.UtcNow - lastOrder.OrderDate).TotalDays : 9999
                };
            })
            .OrderByDescending(c => c.DaysSinceLastOrder)
            .ToList();
    }

    public async Task<List<PaymentReportDto>> GetOutstandingPaymentsAsync(CancellationToken cancellationToken = default)
    {
        var customers = await _unitOfWork.Repository<CustomerProfile>().Query()
            .Include(c => c.Payments)
            .Include(c => c.Orders)
            .ToListAsync(cancellationToken);

        return customers
            .Select(c =>
            {
                var totalInvoiced = c.Orders?.Sum(o => o.TotalAmount) ?? 0;
                var totalPaid = c.Payments?.Sum(p => p.Amount) ?? 0;
                var outstanding = totalInvoiced - totalPaid;
                return new PaymentReportDto
                {
                    CustomerId = c.Id,
                    ShopName = c.ShopName,
                    OutstandingAmount = outstanding,
                    LastPaymentDate = c.Payments?.MaxBy(p => p.PaymentDate)?.PaymentDate,
                    OverdueDays = c.Payments != null && c.Payments.Any() ? (int)(DateTime.UtcNow - c.Payments.Max(p => p.PaymentDate)).TotalDays : 0
                };
            })
            .Where(p => p.OutstandingAmount > 0)
            .OrderByDescending(p => p.OutstandingAmount)
            .ToList();
    }
}
