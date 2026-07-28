using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DistributionSystem.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDiscountPercentToPriceList : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM information_schema.columns
        WHERE table_name = 'PriceLists' AND column_name = 'DiscountPercent'
    ) THEN
        ALTER TABLE ""PriceLists"" ADD COLUMN ""DiscountPercent"" numeric(18,2) NULL;
    END IF;
END $$;
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
DO $$
BEGIN
    IF EXISTS (
        SELECT 1
        FROM information_schema.columns
        WHERE table_name = 'PriceLists' AND column_name = 'DiscountPercent'
    ) THEN
        ALTER TABLE ""PriceLists"" DROP COLUMN ""DiscountPercent"";
    END IF;
END $$;
");
        }
    }
}
