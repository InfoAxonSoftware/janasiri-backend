namespace DistributionSystem.Application.DTOs.Report;

public class DashboardDto
{
    public decimal TotalSalesToday { get; set; }
    public decimal TotalSalesMonth { get; set; }
    public int TotalOrders { get; set; }
    public int PendingOrders { get; set; }
    public int TotalProducts { get; set; }
    public int LowStockProducts { get; set; }
    public int TotalCustomers { get; set; }
    public int ActiveReps { get; set; }
    public decimal TotalOutstanding { get; set; }
    // new quotation metrics
    public int TotalQuotations { get; set; }
    public int PendingQuotations { get; set; }
    public List<SalesTrendDto> SalesTrend { get; set; } = [];
    public List<TopProductDto> TopProducts { get; set; } = [];
}

public class SalesTrendDto
{
    public string Period { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public int OrderCount { get; set; }
}

public class TopProductDto
{
    public Guid ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public int QuantitySold { get; set; }
    public decimal Revenue { get; set; }
}

public class SalesReportDto
{
    public DateTime FromDate { get; set; }
    public DateTime ToDate { get; set; }
    public decimal TotalSales { get; set; }
    public int TotalOrders { get; set; }
    public decimal AverageOrderValue { get; set; }
    public List<SalesTrendDto> DailyBreakdown { get; set; } = [];
}

public class ReportFilterRequest
{
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
    public Guid? RepId { get; set; }
    public Guid? CategoryId { get; set; }
    public int Top { get; set; } = 10;
}

public class CustomerActivityDto
{
    public Guid CustomerId { get; set; }
    public string ShopName { get; set; } = string.Empty;
    public int OrderCount { get; set; }
    public decimal TotalSpent { get; set; }
    public DateTime? LastOrderDate { get; set; }
    public int DaysSinceLastOrder { get; set; }
}

public class PaymentReportDto
{
    public Guid CustomerId { get; set; }
    public string ShopName { get; set; } = string.Empty;
    public decimal OutstandingAmount { get; set; }
    public int OverdueDays { get; set; }
    public DateTime? LastPaymentDate { get; set; }
}
