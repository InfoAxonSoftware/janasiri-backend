using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DistributionSystem.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddRoleSpecificTrashToRepPayments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_RepPayments_RepId",
                table: "RepPayments");

            migrationBuilder.AddColumn<DateTime>(
                name: "CoordinatorDeletedAt",
                table: "RepPayments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeletedByCoordinator",
                table: "RepPayments",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeletedBySalesRep",
                table: "RepPayments",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "SalesRepDeletedAt",
                table: "RepPayments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_RepPayments_IsDeletedByAdmin",
                table: "RepPayments",
                column: "IsDeletedByAdmin");

            migrationBuilder.CreateIndex(
                name: "IX_RepPayments_IsDeletedByCoordinator",
                table: "RepPayments",
                column: "IsDeletedByCoordinator");

            migrationBuilder.CreateIndex(
                name: "IX_RepPayments_RepId_IsDeletedBySalesRep",
                table: "RepPayments",
                columns: new[] { "RepId", "IsDeletedBySalesRep" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_RepPayments_IsDeletedByAdmin",
                table: "RepPayments");

            migrationBuilder.DropIndex(
                name: "IX_RepPayments_IsDeletedByCoordinator",
                table: "RepPayments");

            migrationBuilder.DropIndex(
                name: "IX_RepPayments_RepId_IsDeletedBySalesRep",
                table: "RepPayments");

            migrationBuilder.DropColumn(
                name: "CoordinatorDeletedAt",
                table: "RepPayments");

            migrationBuilder.DropColumn(
                name: "IsDeletedByCoordinator",
                table: "RepPayments");

            migrationBuilder.DropColumn(
                name: "IsDeletedBySalesRep",
                table: "RepPayments");

            migrationBuilder.DropColumn(
                name: "SalesRepDeletedAt",
                table: "RepPayments");

            migrationBuilder.CreateIndex(
                name: "IX_RepPayments_RepId",
                table: "RepPayments",
                column: "RepId");
        }
    }
}
