using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DistributionSystem.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddRepPaymentAdminPurgeAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "AdminPurgedAt",
                table: "RepPayments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsPurgedByAdmin",
                table: "RepPayments",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Never lose permanent Admin hiding decisions for financial evidence.
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM "RepPayments" WHERE "IsPurgedByAdmin") THEN
                        RAISE EXCEPTION 'Unsafe Trash rollback: purged payment reports require reconciliation';
                    END IF;
                END $$;
            """);
            migrationBuilder.DropColumn(
                name: "AdminPurgedAt",
                table: "RepPayments");

            migrationBuilder.DropColumn(
                name: "IsPurgedByAdmin",
                table: "RepPayments");
        }
    }
}
