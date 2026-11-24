using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DistributionSystem.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddQuotationAdminPurgeAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "AdminPurgedAt",
                table: "Quotations",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsPurgedByAdmin",
                table: "Quotations",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // A purged quotation would reappear in old Admin Trash if this
            // visibility marker were silently dropped during rollback.
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM "Quotations" WHERE "IsPurgedByAdmin") THEN
                        RAISE EXCEPTION 'Unsafe Trash rollback: purged quotations require reconciliation';
                    END IF;
                END $$;
            """);
            migrationBuilder.DropColumn(
                name: "AdminPurgedAt",
                table: "Quotations");

            migrationBuilder.DropColumn(
                name: "IsPurgedByAdmin",
                table: "Quotations");
        }
    }
}
