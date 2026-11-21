using DistributionSystem.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DistributionSystem.Infrastructure.Data.Configurations;

public sealed class UserRecordTrashConfiguration : IEntityTypeConfiguration<UserRecordTrash>
{
    public void Configure(EntityTypeBuilder<UserRecordTrash> builder)
    {
        builder.ToTable("UserRecordTrashes");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.RecordKind).HasMaxLength(32).IsRequired();
        // Optimistic concurrency: Restore must not erase an entry another request
        // just purged, and purge must not update a concurrently restored entry.
        builder.Property(x => x.PurgedAt).IsConcurrencyToken();
        builder.HasIndex(x => new { x.UserId, x.RecordKind, x.RecordId }).IsUnique();
        builder.HasIndex(x => new { x.DeletedAt, x.PurgedAt });
        builder.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}
