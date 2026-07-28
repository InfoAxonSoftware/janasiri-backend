using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DistributionSystem.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddRoleIsolatedOrderTrashPurge : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "AdminPurgedAt",
                table: "QuickRequests",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CoordinatorPurgedAt",
                table: "QuickRequests",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsPurgedByAdmin",
                table: "QuickRequests",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsPurgedByCoordinator",
                table: "QuickRequests",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsPurgedByRep",
                table: "QuickRequests",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "RepPurgedAt",
                table: "QuickRequests",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "AdminPurgedAt",
                table: "Orders",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CoordinatorPurgedAt",
                table: "Orders",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsPurgedByAdmin",
                table: "Orders",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsPurgedByCoordinator",
                table: "Orders",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsPurgedByRep",
                table: "Orders",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "RepPurgedAt",
                table: "Orders",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AdminPurgedAt",
                table: "QuickRequests");

            migrationBuilder.DropColumn(
                name: "CoordinatorPurgedAt",
                table: "QuickRequests");

            migrationBuilder.DropColumn(
                name: "IsPurgedByAdmin",
                table: "QuickRequests");

            migrationBuilder.DropColumn(
                name: "IsPurgedByCoordinator",
                table: "QuickRequests");

            migrationBuilder.DropColumn(
                name: "IsPurgedByRep",
                table: "QuickRequests");

            migrationBuilder.DropColumn(
                name: "RepPurgedAt",
                table: "QuickRequests");

            migrationBuilder.DropColumn(
                name: "AdminPurgedAt",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "CoordinatorPurgedAt",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "IsPurgedByAdmin",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "IsPurgedByCoordinator",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "IsPurgedByRep",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "RepPurgedAt",
                table: "Orders");
        }
    }
}
