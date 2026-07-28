using DistributionSystem.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DistributionSystem.Infrastructure.Data.Configurations;

public class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        builder.ToTable("Orders");
        builder.HasKey(o => o.Id);
        builder.HasIndex(o => o.OrderNumber).IsUnique();
        builder.HasIndex(o => o.Status);
        builder.HasIndex(o => o.OrderDate);
        builder.Property(o => o.OrderNumber).HasMaxLength(50).IsRequired();
        builder.Property(o => o.SubTotal).HasPrecision(18, 2);
        builder.Property(o => o.TaxAmount).HasPrecision(18, 2);
        builder.Property(o => o.DiscountAmount).HasPrecision(18, 2);
        builder.Property(o => o.TotalAmount).HasPrecision(18, 2);
        builder.Property(o => o.DeliveryAddress).HasMaxLength(1000);
        builder.Property(o => o.DeliveryNotes).HasMaxLength(500);
        builder.Property(o => o.CancellationReason).HasMaxLength(500);
        builder.Property(o => o.RejectionReason).HasMaxLength(500);
        builder.Property(o => o.RatingComment).HasMaxLength(500);

        builder.HasOne(o => o.Customer).WithMany(c => c.Orders).HasForeignKey(o => o.CustomerId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(o => o.Rep).WithMany(r => r.Orders).HasForeignKey(o => o.RepId).OnDelete(DeleteBehavior.SetNull);
    }
}

public class OrderItemConfiguration : IEntityTypeConfiguration<OrderItem>
{
    public void Configure(EntityTypeBuilder<OrderItem> builder)
    {
        builder.ToTable("OrderItems");
        builder.HasKey(oi => oi.Id);
        // snapshot values copied at order creation; must be required so historical
        // records are never null.
        builder.Property(oi => oi.ProductName).HasMaxLength(200).IsRequired();
        builder.Property(oi => oi.ProductSKU).HasMaxLength(50);
        builder.Property(oi => oi.UnitPrice).HasPrecision(18, 2);
        builder.Property(oi => oi.DiscountPercent).HasPrecision(5, 2);
        builder.Property(oi => oi.TaxAmount).HasPrecision(18, 2);
        builder.Property(oi => oi.LineTotal).HasPrecision(18, 2);

        builder.HasOne(oi => oi.Order).WithMany(o => o.OrderItems).HasForeignKey(oi => oi.OrderId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(oi => oi.Product).WithMany(p => p.OrderItems)
               .HasForeignKey(oi => oi.ProductId)
               .OnDelete(DeleteBehavior.SetNull);
        // allow null FK so the product can be removed without deleting the order items
        builder.Property(oi => oi.ProductId).IsRequired(false);
    }
}
