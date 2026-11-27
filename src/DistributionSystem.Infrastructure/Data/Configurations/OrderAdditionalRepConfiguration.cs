using DistributionSystem.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
namespace DistributionSystem.Infrastructure.Data.Configurations;
public sealed class OrderAdditionalRepConfiguration : IEntityTypeConfiguration<OrderAdditionalRep>
{
    public void Configure(EntityTypeBuilder<OrderAdditionalRep> b)
    {
        b.ToTable("OrderAdditionalReps");
        b.HasKey(a => new { a.OrderId, a.RepId });
        b.HasOne(a => a.Order).WithMany(a => a.AdditionalReps).HasForeignKey(a => a.OrderId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(a => a.Rep).WithMany().HasForeignKey(a => a.RepId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(a => a.RepId);
    }
}