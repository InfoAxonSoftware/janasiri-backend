using DistributionSystem.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
namespace DistributionSystem.Infrastructure.Data.Configurations;
public sealed class QuotationAdditionalRepConfiguration : IEntityTypeConfiguration<QuotationAdditionalRep>
{
    public void Configure(EntityTypeBuilder<QuotationAdditionalRep> b)
    {
        b.ToTable("QuotationAdditionalReps");
        b.HasKey(a => new { a.QuotationId, a.RepId });
        b.HasOne(a => a.Quotation).WithMany(a => a.AdditionalReps).HasForeignKey(a => a.QuotationId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(a => a.Rep).WithMany().HasForeignKey(a => a.RepId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(a => a.RepId);
    }
}