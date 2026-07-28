using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DistributionSystem.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPerRoleSoftDeleteAndNewStatuses : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "CoordinatorDeletedAt",
                table: "Quotations",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CustomerDeletedAt",
                table: "Quotations",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeletedByCoordinator",
                table: "Quotations",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeletedByCustomer",
                table: "Quotations",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeletedByRep",
                table: "Quotations",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "RepDeletedAt",
                table: "Quotations",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "AdminDeletedAt",
                table: "QuickRequests",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeletedByAdmin",
                table: "QuickRequests",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeletedByRep",
                table: "QuickRequests",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "RepDeletedAt",
                table: "QuickRequests",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CoordinatorDeletedAt",
                table: "Orders",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CustomerDeletedAt",
                table: "Orders",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeletedByCoordinator",
                table: "Orders",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeletedByCustomer",
                table: "Orders",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeletedByRep",
                table: "Orders",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "RepDeletedAt",
                table: "Orders",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CoordinatorDeletedAt",
                table: "Quotations");

            migrationBuilder.DropColumn(
                name: "CustomerDeletedAt",
                table: "Quotations");

            migrationBuilder.DropColumn(
                name: "IsDeletedByCoordinator",
                table: "Quotations");

            migrationBuilder.DropColumn(
                name: "IsDeletedByCustomer",
                table: "Quotations");

            migrationBuilder.DropColumn(
                name: "IsDeletedByRep",
                table: "Quotations");

            migrationBuilder.DropColumn(
                name: "RepDeletedAt",
                table: "Quotations");

            migrationBuilder.DropColumn(
                name: "AdminDeletedAt",
                table: "QuickRequests");

            migrationBuilder.DropColumn(
                name: "IsDeletedByAdmin",
                table: "QuickRequests");

            migrationBuilder.DropColumn(
                name: "IsDeletedByRep",
                table: "QuickRequests");

            migrationBuilder.DropColumn(
                name: "RepDeletedAt",
                table: "QuickRequests");

            migrationBuilder.DropColumn(
                name: "CoordinatorDeletedAt",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "CustomerDeletedAt",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "IsDeletedByCoordinator",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "IsDeletedByCustomer",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "IsDeletedByRep",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "RepDeletedAt",
                table: "Orders");
        }
    }
}
