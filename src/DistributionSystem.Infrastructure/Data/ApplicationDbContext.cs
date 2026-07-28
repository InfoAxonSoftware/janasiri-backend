using DistributionSystem.Domain.Entities;
using DistributionSystem.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace DistributionSystem.Infrastructure.Data;

public class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options) { }

    public DbSet<User> Users => Set<User>();
    public DbSet<AdminProfile> AdminProfiles => Set<AdminProfile>();
    public DbSet<SalesRepProfile> SalesRepProfiles => Set<SalesRepProfile>();
    public DbSet<CustomerProfile> CustomerProfiles => Set<CustomerProfile>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    public DbSet<StockMovement> StockMovements => Set<StockMovement>();
    public DbSet<Route> Routes => Set<Route>();
    public DbSet<RepRoute> RepRoutes => Set<RepRoute>();
    public DbSet<RouteCustomer> RouteCustomers => Set<RouteCustomer>();
    public DbSet<Visit> Visits => Set<Visit>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<PaymentAllocation> PaymentAllocations => Set<PaymentAllocation>();
    public DbSet<Promotion> Promotions => Set<Promotion>();
    public DbSet<PriceList> PriceLists => Set<PriceList>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<SalesTarget> SalesTargets => Set<SalesTarget>();
    public DbSet<Complaint> Complaints => Set<Complaint>();
    public DbSet<ComplaintMessage> ComplaintMessages => Set<ComplaintMessage>();
    public DbSet<CartItem> CartItems => Set<CartItem>();
    public DbSet<FavoriteProduct> FavoriteProducts => Set<FavoriteProduct>();
    public DbSet<CoordinatorProfile> CoordinatorProfiles => Set<CoordinatorProfile>();
    public DbSet<Quotation> Quotations => Set<Quotation>();
    public DbSet<QuotationItem> QuotationItems => Set<QuotationItem>();
    public DbSet<SystemConfiguration> SystemConfigurations => Set<SystemConfiguration>();
    public DbSet<CustomerRegistrationRequest> CustomerRegistrationRequests => Set<CustomerRegistrationRequest>();
    public DbSet<Region> Regions => Set<Region>();
    public DbSet<SubRegion> SubRegions => Set<SubRegion>();
    public DbSet<RepRegion> RepRegions => Set<RepRegion>();
    public DbSet<RepSubRegion> RepSubRegions => Set<RepSubRegion>();
    public DbSet<RepCoordinator> RepCoordinators => Set<RepCoordinator>();
    public DbSet<GalleryItem> GalleryItems => Set<GalleryItem>();
    public DbSet<OutstandingReport> OutstandingReports => Set<OutstandingReport>();
    public DbSet<OutstandingEntry> OutstandingEntries => Set<OutstandingEntry>();
    public DbSet<StockReport> StockReports => Set<StockReport>();
    public DbSet<StockReportRow> StockReportRows => Set<StockReportRow>();
    public DbSet<SalesSummaryReport> SalesSummaryReports => Set<SalesSummaryReport>();
    public DbSet<SalesSummaryEntry> SalesSummaryEntries => Set<SalesSummaryEntry>();
    public DbSet<TargetSalesReport> TargetSalesReports => Set<TargetSalesReport>();
    public DbSet<TargetSalesReportEntry> TargetSalesReportEntries => Set<TargetSalesReportEntry>();
    public DbSet<QuickRequest> QuickRequests => Set<QuickRequest>();
    public DbSet<QuickRequestImage> QuickRequestImages => Set<QuickRequestImage>();
    public DbSet<RepPayment> RepPayments => Set<RepPayment>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);
    }

    public override int SaveChanges()
    {
        UpdateAuditFields();
        return base.SaveChanges();
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        UpdateAuditFields();
        return base.SaveChangesAsync(cancellationToken);
    }

    private void UpdateAuditFields()
    {
        var entries = ChangeTracker.Entries<IAuditableEntity>();
        foreach (var entry in entries)
        {
            if (entry.State == EntityState.Added)
            {
                entry.Entity.CreatedAt = DateTime.UtcNow;
            }
            if (entry.State == EntityState.Modified)
            {
                entry.Entity.UpdatedAt = DateTime.UtcNow;
            }
        }
    }
}
