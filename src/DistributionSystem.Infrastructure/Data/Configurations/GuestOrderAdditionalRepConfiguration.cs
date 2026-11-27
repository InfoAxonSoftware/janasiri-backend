using DistributionSystem.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DistributionSystem.Infrastructure.Data.Configurations;

public sealed class GuestOrderAdditionalRepConfiguration : IEntityTypeConfiguration<GuestOrderAdditionalRep>
{
    public void Configure(EntityTypeBuilder<GuestOrderAdditionalRep> builder)
    {
        builder.ToTable("GuestOrderAdditionalReps");
        builder.HasKey(x => new { x.QuickRequestId, x.RepId });
        builder.HasOne(x => x.QuickRequest).WithMany(x => x.AdditionalReps)
            .HasForeignKey(x => x.QuickRequestId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.Rep).WithMany().HasForeignKey(x => x.RepId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => x.RepId);
    }
}
