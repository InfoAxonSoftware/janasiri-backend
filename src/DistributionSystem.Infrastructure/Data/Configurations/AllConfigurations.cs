using DistributionSystem.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DistributionSystem.Infrastructure.Data.Configurations;

public class RepRegionConfiguration : IEntityTypeConfiguration<RepRegion>
{
    public void Configure(EntityTypeBuilder<RepRegion> builder)
    {
        builder.ToTable("RepRegions");
        builder.HasKey(rr => rr.Id);
        builder.HasIndex(rr => new { rr.RepId, rr.RegionId }).IsUnique();

        builder.HasOne(rr => rr.Rep)
            .WithMany(r => r.Regions)
            .HasForeignKey(rr => rr.RepId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(rr => rr.Region)
            .WithMany()
            .HasForeignKey(rr => rr.RegionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class RepSubRegionConfiguration : IEntityTypeConfiguration<RepSubRegion>
{
    public void Configure(EntityTypeBuilder<RepSubRegion> builder)
    {
        builder.ToTable("RepSubRegions");
        builder.HasKey(rs => rs.Id);
        builder.HasIndex(rs => new { rs.RepId, rs.SubRegionId }).IsUnique();

        builder.HasOne(rs => rs.Rep)
            .WithMany(r => r.SubRegions)
            .HasForeignKey(rs => rs.RepId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(rs => rs.SubRegion)
            .WithMany()
            .HasForeignKey(rs => rs.SubRegionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class RepCoordinatorConfiguration : IEntityTypeConfiguration<RepCoordinator>
{
    public void Configure(EntityTypeBuilder<RepCoordinator> builder)
    {
        builder.ToTable("RepCoordinators");
        builder.HasKey(rc => rc.Id);
        builder.HasIndex(rc => new { rc.RepId, rc.CoordinatorId }).IsUnique();

        builder.HasOne(rc => rc.Rep)
            .WithMany(r => r.Coordinators)
            .HasForeignKey(rc => rc.RepId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(rc => rc.Coordinator)
            .WithMany(c => c.RepCoordinators)
            .HasForeignKey(rc => rc.CoordinatorId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class StockMovementConfiguration : IEntityTypeConfiguration<StockMovement>
{
    public void Configure(EntityTypeBuilder<StockMovement> builder)
    {
        builder.ToTable("StockMovements");
        builder.HasKey(s => s.Id);
        builder.HasIndex(s => s.ProductId);
        builder.Property(s => s.Reason).HasMaxLength(500);
        builder.Property(s => s.ReferenceNumber).HasMaxLength(100);
        builder.HasOne(s => s.Product).WithMany().HasForeignKey(s => s.ProductId);
    }
}

public class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        builder.ToTable("Payments");
        builder.HasKey(p => p.Id);
        builder.HasIndex(p => p.CustomerId);
        builder.Property(p => p.Amount).HasPrecision(18, 2);
        builder.Property(p => p.ReferenceNumber).HasMaxLength(100);
        builder.Property(p => p.ChequeNumber).HasMaxLength(50);
        builder.Property(p => p.BankName).HasMaxLength(200);
        builder.Property(p => p.Notes).HasMaxLength(500);

        builder.HasOne(p => p.Customer).WithMany(c => c.Payments).HasForeignKey(p => p.CustomerId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(p => p.Order).WithMany(o => o.Payments).HasForeignKey(p => p.OrderId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne(p => p.CollectedByRep).WithMany(r => r.CollectedPayments).HasForeignKey(p => p.CollectedByRepId).OnDelete(DeleteBehavior.SetNull);
    }
}

public class PaymentAllocationConfiguration : IEntityTypeConfiguration<PaymentAllocation>
{
    public void Configure(EntityTypeBuilder<PaymentAllocation> builder)
    {
        builder.ToTable("PaymentAllocations");
        builder.HasKey(pa => pa.Id);
        builder.Property(pa => pa.AllocatedAmount).HasPrecision(18, 2);
        builder.HasOne(pa => pa.Payment).WithMany(p => p.Allocations).HasForeignKey(pa => pa.PaymentId);
        builder.HasOne(pa => pa.Order).WithMany().HasForeignKey(pa => pa.OrderId);
    }
}

public class QuickRequestConfiguration : IEntityTypeConfiguration<QuickRequest>
{
    public void Configure(EntityTypeBuilder<QuickRequest> builder)
    {
        builder.Property(q => q.RequestNumber).HasMaxLength(20);
    }
}

public class RepPaymentConfiguration : IEntityTypeConfiguration<RepPayment>
{
    public void Configure(EntityTypeBuilder<RepPayment> builder)
    {
        builder.ToTable("RepPayments");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.CustomerName).HasMaxLength(200).IsRequired();
        builder.Property(r => r.ReferenceNumber).HasMaxLength(100);
        builder.Property(r => r.Amount).HasPrecision(18, 2);
        builder.Property(r => r.ImageUrl).HasMaxLength(500);
        builder.Property(r => r.AdminNotes).HasMaxLength(1000);
        builder.Property(r => r.CreatedBy).HasMaxLength(200);
        builder.Property(r => r.UpdatedBy).HasMaxLength(200);

        builder.HasIndex(r => new { r.RepId, r.IsDeletedBySalesRep });
        builder.HasIndex(r => r.IsDeletedByAdmin);
        builder.HasIndex(r => r.IsDeletedByCoordinator);

        builder.HasOne(r => r.Rep)
            .WithMany()
            .HasForeignKey(r => r.RepId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class RouteConfiguration : IEntityTypeConfiguration<Route>
{
    public void Configure(EntityTypeBuilder<Route> builder)
    {
        builder.ToTable("Routes");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Name).HasMaxLength(200).IsRequired();
        builder.Property(r => r.Description).HasMaxLength(500);
    }
}

public class RepRouteConfiguration : IEntityTypeConfiguration<RepRoute>
{
    public void Configure(EntityTypeBuilder<RepRoute> builder)
    {
        builder.ToTable("RepRoutes");
        builder.HasKey(rr => rr.Id);
        builder.HasIndex(rr => new { rr.RouteId, rr.RepId }).IsUnique();

        builder.HasOne(rr => rr.Route)
            .WithMany(r => r.AssignedReps)
            .HasForeignKey(rr => rr.RouteId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(rr => rr.Rep)
            .WithMany(r => r.AssignedRoutes)
            .HasForeignKey(rr => rr.RepId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class RouteCustomerConfiguration : IEntityTypeConfiguration<RouteCustomer>
{
    public void Configure(EntityTypeBuilder<RouteCustomer> builder)
    {
        builder.ToTable("RouteCustomers");
        builder.HasKey(rc => rc.Id);
        builder.HasIndex(rc => new { rc.RouteId, rc.CustomerId }).IsUnique();
        builder.Property(rc => rc.VisitFrequency).HasMaxLength(20);
        builder.HasOne(rc => rc.Route).WithMany(r => r.RouteCustomers).HasForeignKey(rc => rc.RouteId);
        builder.HasOne(rc => rc.Customer).WithMany().HasForeignKey(rc => rc.CustomerId);
    }
}

public class QuotationItemConfiguration : IEntityTypeConfiguration<QuotationItem>
{
    public void Configure(EntityTypeBuilder<QuotationItem> builder)
    {
        builder.ToTable("QuotationItems");
        builder.HasKey(qi => qi.Id);
        builder.Property(qi => qi.ProductName).HasMaxLength(300).IsRequired();
        builder.Property(qi => qi.ProductSKU).HasMaxLength(50);
        builder.Property(qi => qi.UnitPrice).HasPrecision(18, 2);
        builder.Property(qi => qi.ExpectedPrice).HasPrecision(18, 2);
        builder.Property(qi => qi.DiscountPercent).HasPrecision(5, 2);
        builder.Property(qi => qi.TaxAmount).HasPrecision(18, 2);
        builder.Property(qi => qi.LineTotal).HasPrecision(18, 2);

        builder.HasOne(qi => qi.Quotation)
            .WithMany(q => q.Items)
            .HasForeignKey(qi => qi.QuotationId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(qi => qi.Product)
            .WithMany()
            .HasForeignKey(qi => qi.ProductId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

public class VisitConfiguration : IEntityTypeConfiguration<Visit>
{
    public void Configure(EntityTypeBuilder<Visit> builder)
    {
        builder.ToTable("Visits");
        builder.HasKey(v => v.Id);
        builder.HasIndex(v => new { v.RepId, v.PlannedDate });
        builder.Property(v => v.Notes).HasMaxLength(1000);
        builder.Property(v => v.PaymentsCollected).HasPrecision(18, 2);
        builder.OwnsOne(v => v.CheckInLocation, l =>
        {
            l.Property(x => x.Latitude).HasColumnName("CheckIn_Latitude");
            l.Property(x => x.Longitude).HasColumnName("CheckIn_Longitude");
        });
        builder.OwnsOne(v => v.CheckOutLocation, l =>
        {
            l.Property(x => x.Latitude).HasColumnName("CheckOut_Latitude");
            l.Property(x => x.Longitude).HasColumnName("CheckOut_Longitude");
        });
        builder.HasOne(v => v.Rep).WithMany(r => r.Visits).HasForeignKey(v => v.RepId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(v => v.Customer).WithMany().HasForeignKey(v => v.CustomerId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(v => v.Route).WithMany(r => r.Visits).HasForeignKey(v => v.RouteId).OnDelete(DeleteBehavior.SetNull);
    }
}

public class PromotionConfiguration : IEntityTypeConfiguration<Promotion>
{
    public void Configure(EntityTypeBuilder<Promotion> builder)
    {
        builder.ToTable("Promotions");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Name).HasMaxLength(200).IsRequired();
        builder.Property(p => p.Description).HasMaxLength(500);
        builder.Property(p => p.PromotionType).HasMaxLength(50);
        builder.Property(p => p.DiscountPercent).HasPrecision(5, 2);
    }
}

public class PriceListConfiguration : IEntityTypeConfiguration<PriceList>
{
    public void Configure(EntityTypeBuilder<PriceList> builder)
    {
        builder.ToTable("PriceLists");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.CustomerSegment).HasMaxLength(64);
        builder.Property(p => p.SpecialPrice).HasPrecision(18, 2);
        builder.HasOne(p => p.Product).WithMany().HasForeignKey(p => p.ProductId);
    }
}

public class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> builder)
    {
        builder.ToTable("Notifications");
        builder.HasKey(n => n.Id);
        builder.HasIndex(n => new { n.UserId, n.IsRead });
        builder.Property(n => n.Title).HasMaxLength(300).IsRequired();
        builder.Property(n => n.Message).HasMaxLength(2000).IsRequired();
        builder.HasOne(n => n.User).WithMany(u => u.Notifications).HasForeignKey(n => n.UserId);
    }
}

public class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        builder.ToTable("AuditLogs");
        builder.HasKey(a => a.Id);
        builder.HasIndex(a => a.UserId);
        builder.HasIndex(a => a.CreatedAt);
        builder.Property(a => a.Action).HasMaxLength(200).IsRequired();
        builder.Property(a => a.EntityType).HasMaxLength(100);
        builder.Property(a => a.EntityId).HasMaxLength(100);
        builder.Property(a => a.IpAddress).HasMaxLength(50);
        builder.Property(a => a.UserAgent).HasMaxLength(500);
        builder.HasOne(a => a.User).WithMany(u => u.AuditLogs).HasForeignKey(a => a.UserId).OnDelete(DeleteBehavior.SetNull);
    }
}

public class SalesTargetConfiguration : IEntityTypeConfiguration<SalesTarget>
{
    public void Configure(EntityTypeBuilder<SalesTarget> builder)
    {
        builder.ToTable("SalesTargets");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.TargetName).HasMaxLength(100);
        builder.Property(s => s.TargetPeriod).HasMaxLength(20);
        builder.Property(s => s.TargetAmount).HasPrecision(18, 2);
        builder.Property(s => s.AchievedAmount).HasPrecision(18, 2);
        builder.Property(s => s.Status).HasMaxLength(20);
        builder.HasOne(s => s.Rep).WithMany(r => r.SalesTargets).HasForeignKey(s => s.RepId);
    }
}

public class ComplaintConfiguration : IEntityTypeConfiguration<Complaint>
{
    public void Configure(EntityTypeBuilder<Complaint> builder)
    {
        builder.ToTable("Complaints");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Subject).HasMaxLength(300).IsRequired();
        builder.Property(c => c.Description).HasMaxLength(2000).IsRequired();
        builder.Property(c => c.CreatedByRole).HasMaxLength(50).IsRequired();
        builder.HasOne(c => c.Customer).WithMany(cu => cu.Complaints).HasForeignKey(c => c.CustomerId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne(c => c.Order).WithMany().HasForeignKey(c => c.OrderId).OnDelete(DeleteBehavior.SetNull);
        builder.HasMany(c => c.Messages).WithOne(m => m.Complaint).HasForeignKey(m => m.ComplaintId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class ComplaintMessageConfiguration : IEntityTypeConfiguration<ComplaintMessage>
{
    public void Configure(EntityTypeBuilder<ComplaintMessage> builder)
    {
        builder.ToTable("ComplaintMessages");
        builder.HasKey(m => m.Id);
        builder.Property(m => m.SenderRole).HasMaxLength(50).IsRequired();
        builder.Property(m => m.Message).HasMaxLength(4000).IsRequired();
        builder.HasOne(m => m.SenderUser)
            .WithMany()
            .HasForeignKey(m => m.SenderUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class CartItemConfiguration : IEntityTypeConfiguration<CartItem>
{
    public void Configure(EntityTypeBuilder<CartItem> builder)
    {
        builder.ToTable("CartItems");
        builder.HasKey(c => c.Id);
        builder.HasIndex(c => new { c.CustomerId, c.ProductId }).IsUnique();
        builder.HasOne(c => c.Customer).WithMany(cu => cu.CartItems).HasForeignKey(c => c.CustomerId);
        builder.HasOne(c => c.Product).WithMany().HasForeignKey(c => c.ProductId);
    }
}

public class FavoriteProductConfiguration : IEntityTypeConfiguration<FavoriteProduct>
{
    public void Configure(EntityTypeBuilder<FavoriteProduct> builder)
    {
        builder.ToTable("FavoriteProducts");
        builder.HasKey(f => f.Id);
        builder.HasIndex(f => new { f.CustomerId, f.ProductId }).IsUnique();
        builder.HasOne(f => f.Customer).WithMany(c => c.FavoriteProducts).HasForeignKey(f => f.CustomerId);
        builder.HasOne(f => f.Product).WithMany().HasForeignKey(f => f.ProductId);
    }
}

public class CustomerRegistrationRequestConfiguration : IEntityTypeConfiguration<CustomerRegistrationRequest>
{
    public void Configure(EntityTypeBuilder<CustomerRegistrationRequest> builder)
    {
        builder.ToTable("CustomerRegistrationRequests");
        builder.HasKey(r => r.Id);
        builder.HasIndex(r => r.Email);
        builder.HasIndex(r => r.Status);
        builder.Property(r => r.CustomerType).HasMaxLength(20).IsRequired();
        builder.Property(r => r.CustomerName).HasMaxLength(300).IsRequired();
        builder.Property(r => r.BusinessRegistrationNumber).HasMaxLength(100);
        builder.Property(r => r.Email).HasMaxLength(256).IsRequired();
        builder.Property(r => r.Telephone).HasMaxLength(50);
        builder.Property(r => r.Status).HasMaxLength(20).IsRequired().HasDefaultValue("Pending");
        builder.Property(r => r.RegisteredAddress).HasMaxLength(500);
        builder.Property(r => r.BusinessName).HasMaxLength(300);
        builder.Property(r => r.BusinessLocation).HasMaxLength(500);
        builder.Property(r => r.BankBranch).HasMaxLength(300);
        builder.Property(r => r.Province).HasMaxLength(100);
        builder.Property(r => r.Town).HasMaxLength(200);
        builder.Property(r => r.BusinessRegDocPath).HasMaxLength(1000);
        builder.Property(r => r.BusinessAddressDocPath).HasMaxLength(1000);
        builder.Property(r => r.VatDocPath).HasMaxLength(1000);
        builder.Property(r => r.RejectionReason).HasMaxLength(1000);
        builder.Property(r => r.ReviewNotes).HasMaxLength(1000);
        builder.HasOne(r => r.AssignedCoordinator)
            .WithMany()
            .HasForeignKey(r => r.AssignedCoordinatorId)
            .OnDelete(DeleteBehavior.SetNull);
        builder.HasOne(r => r.AssignedRep)
            .WithMany()
            .HasForeignKey(r => r.AssignedRepId)
            .OnDelete(DeleteBehavior.SetNull);
        builder.HasOne(r => r.Region)
            .WithMany()
            .HasForeignKey(r => r.RegionId)
            .OnDelete(DeleteBehavior.SetNull);
        builder.HasOne(r => r.SubRegion)
            .WithMany()
            .HasForeignKey(r => r.SubRegionId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

public class RegionConfiguration : IEntityTypeConfiguration<Region>
{
    public void Configure(EntityTypeBuilder<Region> builder)
    {
        builder.ToTable("Regions");
        builder.HasKey(r => r.Id);
        builder.HasIndex(r => r.Name).IsUnique();
        builder.Property(r => r.Name).HasMaxLength(200).IsRequired();
    }
}

public class SubRegionConfiguration : IEntityTypeConfiguration<SubRegion>
{
    public void Configure(EntityTypeBuilder<SubRegion> builder)
    {
        builder.ToTable("SubRegions");
        builder.HasKey(s => s.Id);
        builder.HasIndex(s => new { s.RegionId, s.Name }).IsUnique();
        builder.Property(s => s.Name).HasMaxLength(200).IsRequired();
        builder.HasOne(s => s.Region).WithMany(r => r.SubRegions).HasForeignKey(s => s.RegionId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class CoordinatorProfileConfiguration : IEntityTypeConfiguration<CoordinatorProfile>
{
    public void Configure(EntityTypeBuilder<CoordinatorProfile> builder)
    {
        builder.ToTable("CoordinatorProfiles");
        builder.HasKey(c => c.Id);
        builder.HasIndex(c => c.EmployeeCode).IsUnique();
        builder.Property(c => c.FullName).HasMaxLength(200).IsRequired();
        builder.Property(c => c.EmployeeCode).HasMaxLength(20).IsRequired();
        builder.HasOne(c => c.Region).WithMany(r => r.Coordinators).HasForeignKey(c => c.RegionId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne(c => c.User).WithOne(u => u.CoordinatorProfile).HasForeignKey<CoordinatorProfile>(c => c.UserId);
    }
}

public class OutstandingReportConfiguration : IEntityTypeConfiguration<OutstandingReport>
{
    public void Configure(EntityTypeBuilder<OutstandingReport> builder)
    {
        builder.ToTable("OutstandingReports");
        builder.HasKey(r => r.Id);
        builder.HasIndex(r => r.RegionId).IsUnique();
        builder.Property(r => r.RegionName).HasMaxLength(200).IsRequired();
        builder.Property(r => r.UploadedBy).HasMaxLength(200);

        builder.HasOne(r => r.Region)
            .WithMany()
            .HasForeignKey(r => r.RegionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(r => r.Entries)
            .WithOne(e => e.Report)
            .HasForeignKey(e => e.OutstandingReportId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class OutstandingEntryConfiguration : IEntityTypeConfiguration<OutstandingEntry>
{
    public void Configure(EntityTypeBuilder<OutstandingEntry> builder)
    {
        builder.ToTable("OutstandingEntries");
        builder.HasKey(e => e.Id);
        builder.HasIndex(e => e.OutstandingReportId);
        builder.HasIndex(e => new { e.OutstandingReportId, e.CustomerName });

        builder.Property(e => e.CustomerName).HasMaxLength(300).IsRequired();
        builder.Property(e => e.TxnType).HasMaxLength(100);
        builder.Property(e => e.RefNo).HasMaxLength(50);

        builder.Property(e => e.Current).HasPrecision(18, 2);
        builder.Property(e => e.Bucket1_15).HasPrecision(18, 2);
        builder.Property(e => e.Bucket16_30).HasPrecision(18, 2);
        builder.Property(e => e.Bucket31_45).HasPrecision(18, 2);
        builder.Property(e => e.Above45).HasPrecision(18, 2);
        builder.Property(e => e.Balance).HasPrecision(18, 2);
    }
}

public class StockReportConfiguration : IEntityTypeConfiguration<StockReport>
{
    public void Configure(EntityTypeBuilder<StockReport> builder)
    {
        builder.ToTable("StockReports");
        builder.HasKey(r => r.Id);
        builder.HasIndex(r => r.RegionId).IsUnique();
        builder.HasIndex(r => r.UploadedAt);
        builder.Property(r => r.RegionName).HasMaxLength(200).IsRequired();
        builder.Property(r => r.CompanyName).HasMaxLength(300);
        builder.Property(r => r.ReportTitle).HasMaxLength(300);
        builder.Property(r => r.OriginalFileName).HasMaxLength(260);
        builder.Property(r => r.UploadedBy).HasMaxLength(200);
        builder.Property(r => r.TotalOnHand).HasPrecision(18, 2);
        builder.Property(r => r.TotalAmount).HasPrecision(18, 2);

        builder.HasOne(r => r.Region)
            .WithMany()
            .HasForeignKey(r => r.RegionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(r => r.Rows)
            .WithOne(row => row.Report)
            .HasForeignKey(row => row.StockReportId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class StockReportRowConfiguration : IEntityTypeConfiguration<StockReportRow>
{
    public void Configure(EntityTypeBuilder<StockReportRow> builder)
    {
        builder.ToTable("StockReportRows");
        builder.HasKey(r => r.Id);
        builder.HasIndex(r => r.StockReportId);
        builder.HasIndex(r => new { r.StockReportId, r.SortOrder });

        builder.Property(r => r.GroupName).HasMaxLength(300);
        builder.Property(r => r.Item).HasMaxLength(50);
        builder.Property(r => r.SalesDescription).HasMaxLength(500);
        builder.Property(r => r.CostExVat).HasPrecision(18, 2);
        builder.Property(r => r.OnHand).HasPrecision(18, 2);
        builder.Property(r => r.Amount).HasPrecision(18, 2);
    }
}

public class SalesSummaryReportConfiguration : IEntityTypeConfiguration<SalesSummaryReport>
{
    public void Configure(EntityTypeBuilder<SalesSummaryReport> builder)
    {
        builder.ToTable("SalesSummaryReports");
        builder.HasKey(r => r.Id);
        // Duplicate-prevention: one report per region + reporting period.
        builder.HasIndex(r => new { r.RegionId, r.PeriodFrom, r.PeriodTo }).IsUnique();
        builder.Property(r => r.RegionName).HasMaxLength(200).IsRequired();
        builder.Property(r => r.OriginalFileName).HasMaxLength(300);
        builder.Property(r => r.UploadedBy).HasMaxLength(200);

        builder.HasOne(r => r.Region)
            .WithMany()
            .HasForeignKey(r => r.RegionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(r => r.Entries)
            .WithOne(e => e.Report)
            .HasForeignKey(e => e.SalesSummaryReportId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class SalesSummaryEntryConfiguration : IEntityTypeConfiguration<SalesSummaryEntry>
{
    public void Configure(EntityTypeBuilder<SalesSummaryEntry> builder)
    {
        builder.ToTable("SalesSummaryEntries");
        builder.HasKey(e => e.Id);
        builder.HasIndex(e => e.SalesSummaryReportId);

        builder.Property(e => e.GroupName).HasMaxLength(200).IsRequired();
        builder.Property(e => e.SalesWithTax).HasPrecision(18, 2);
        builder.Property(e => e.Tax).HasPrecision(18, 2);
        builder.Property(e => e.NetSales).HasPrecision(18, 2);
        builder.Property(e => e.Discount).HasPrecision(18, 2);
        builder.Property(e => e.GrossSales).HasPrecision(18, 2);
    }
}

public class TargetSalesReportConfiguration : IEntityTypeConfiguration<TargetSalesReport>
{
    public void Configure(EntityTypeBuilder<TargetSalesReport> builder)
    {
        builder.ToTable("TargetSalesReports");
        builder.HasKey(r => r.Id);
        builder.HasIndex(r => new { r.TargetId, r.IsCurrent });
        builder.HasIndex(r => r.RepId);
        builder.Property(r => r.OriginalFileName).HasMaxLength(300);
        builder.Property(r => r.UploadedBy).HasMaxLength(200);
        builder.Property(r => r.ActualSales).HasPrecision(18, 2);

        builder.HasOne(r => r.Target)
            .WithMany()
            .HasForeignKey(r => r.TargetId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(r => r.Entries)
            .WithOne(e => e.Report)
            .HasForeignKey(e => e.TargetSalesReportId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class TargetSalesReportEntryConfiguration : IEntityTypeConfiguration<TargetSalesReportEntry>
{
    public void Configure(EntityTypeBuilder<TargetSalesReportEntry> builder)
    {
        builder.ToTable("TargetSalesReportEntries");
        builder.HasKey(e => e.Id);
        builder.HasIndex(e => e.TargetSalesReportId);

        builder.Property(e => e.RefNo).HasMaxLength(50);
        builder.Property(e => e.CustomerName).HasMaxLength(300);
        builder.Property(e => e.ItemDescription).HasMaxLength(300);
        builder.Property(e => e.Qty).HasPrecision(18, 2);
        builder.Property(e => e.Discount).HasPrecision(18, 2);
        builder.Property(e => e.SalesWithTax).HasPrecision(18, 2);
    }
}