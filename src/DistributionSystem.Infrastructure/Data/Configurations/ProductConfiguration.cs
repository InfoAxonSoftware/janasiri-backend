using DistributionSystem.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DistributionSystem.Infrastructure.Data.Configurations;

public class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.ToTable("Products");
        builder.HasKey(p => p.Id);
        builder.HasIndex(p => p.SKU).IsUnique();
        builder.HasIndex(p => p.Barcode);
        builder.Property(p => p.Name).HasMaxLength(300).IsRequired();
        builder.Property(p => p.SKU).HasMaxLength(50).IsRequired();
        builder.Property(p => p.Barcode).HasMaxLength(50);
        builder.Property(p => p.Brand).HasMaxLength(200);
        builder.Property(p => p.SellingPrice).HasPrecision(18, 2);
        builder.Property(p => p.Quantity);

        builder.Property(p => p.DiscountPercent).HasPrecision(5,2);
        builder.Property(p => p.DiscountAmount).HasPrecision(18,2);
        builder.Property(p => p.TaxCode).HasMaxLength(50);
        builder.Property(p => p.TaxAmount).HasPrecision(18,2);
        builder.Property(p => p.TotalAmount).HasPrecision(18,2);
        builder.Property(p => p.UOM).HasMaxLength(100);

        builder.HasOne(p => p.Category).WithMany(c => c.Products).HasForeignKey(p => p.CategoryId).IsRequired(false);
    }
}

public class CategoryConfiguration : IEntityTypeConfiguration<Category>
{
    public void Configure(EntityTypeBuilder<Category> builder)
    {
        builder.ToTable("Categories");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Name).HasMaxLength(200).IsRequired();
        builder.Property(c => c.Description).HasMaxLength(500);

        builder.HasOne(c => c.ParentCategory)
            .WithMany(c => c.SubCategories)
            .HasForeignKey(c => c.ParentCategoryId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
