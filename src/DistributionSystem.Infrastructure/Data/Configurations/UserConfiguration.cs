using DistributionSystem.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DistributionSystem.Infrastructure.Data.Configurations;

public class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("Users");
        builder.HasKey(u => u.Id);
        builder.HasIndex(u => u.Username).IsUnique();
        builder.HasIndex(u => u.Email).IsUnique();
        builder.Property(u => u.Username).HasMaxLength(100).IsRequired();
        builder.Property(u => u.Email).HasMaxLength(200).IsRequired();
        builder.Property(u => u.PasswordHash).IsRequired();
        builder.Property(u => u.CurrentPassword).HasMaxLength(200);
        builder.Property(u => u.PhoneNumber).HasMaxLength(20);
        builder.Property(u => u.RefreshToken).HasMaxLength(500);
        builder.Property(u => u.TokenVersion).IsRequired().HasDefaultValue(0);
        builder.Property(u => u.PasswordChangedAt);

        builder.HasOne(u => u.AdminProfile).WithOne(a => a.User).HasForeignKey<AdminProfile>(a => a.UserId);
        builder.HasOne(u => u.SalesRepProfile).WithOne(s => s.User).HasForeignKey<SalesRepProfile>(s => s.UserId);
        builder.HasOne(u => u.CustomerProfile).WithOne(c => c.User).HasForeignKey<CustomerProfile>(c => c.UserId);
    }
}

public class AdminProfileConfiguration : IEntityTypeConfiguration<AdminProfile>
{
    public void Configure(EntityTypeBuilder<AdminProfile> builder)
    {
        builder.ToTable("AdminProfiles");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.FullName).HasMaxLength(200).IsRequired();
        builder.Property(a => a.Department).HasMaxLength(100);
    }
}

public class SalesRepProfileConfiguration : IEntityTypeConfiguration<SalesRepProfile>
{
    public void Configure(EntityTypeBuilder<SalesRepProfile> builder)
    {
        builder.ToTable("SalesRepProfiles");
        builder.HasKey(s => s.Id);
        builder.HasIndex(s => s.EmployeeCode).IsUnique();
        builder.Property(s => s.FullName).HasMaxLength(200).IsRequired();
        builder.Property(s => s.EmployeeCode).HasMaxLength(20).IsRequired();
    }
}

public class CustomerProfileConfiguration : IEntityTypeConfiguration<CustomerProfile>
{
    public void Configure(EntityTypeBuilder<CustomerProfile> builder)
    {
        builder.ToTable("CustomerProfiles");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.ShopName).HasMaxLength(300).IsRequired();
        builder.Property(c => c.BusinessRegistrationNumber).HasMaxLength(50);
        builder.OwnsOne(c => c.Address, a =>
        {
            a.Property(x => x.Street).HasMaxLength(300).HasColumnName("Address_Street");
            a.Property(x => x.City).HasMaxLength(100).HasColumnName("Address_City");
            a.Property(x => x.State).HasMaxLength(100).HasColumnName("Address_State");
            a.Property(x => x.PostalCode).HasMaxLength(20).HasColumnName("Address_PostalCode");
            a.Property(x => x.Country).HasMaxLength(100).HasColumnName("Address_Country");
        });
        builder.OwnsOne(c => c.Location, l =>
        {
            l.Property(x => x.Latitude).HasColumnName("Location_Latitude");
            l.Property(x => x.Longitude).HasColumnName("Location_Longitude");
        });

        builder.HasOne(c => c.Region).WithMany().HasForeignKey(c => c.RegionId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne(c => c.SubRegion).WithMany().HasForeignKey(c => c.SubRegionId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne(c => c.AssignedRep)
            .WithMany(r => r.AssignedCustomers)
            .HasForeignKey(c => c.AssignedRepId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
