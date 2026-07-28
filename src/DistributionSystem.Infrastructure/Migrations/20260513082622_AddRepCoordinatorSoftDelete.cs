using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DistributionSystem.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddRepCoordinatorSoftDelete : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedAt",
                table: "SalesRepProfiles",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeletedBy",
                table: "SalesRepProfiles",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "SalesRepProfiles",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedAt",
                table: "CoordinatorProfiles",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeletedBy",
                table: "CoordinatorProfiles",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "CoordinatorProfiles",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DeletedAt",
                table: "SalesRepProfiles");

            migrationBuilder.DropColumn(
                name: "DeletedBy",
                table: "SalesRepProfiles");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "SalesRepProfiles");

            migrationBuilder.DropColumn(
                name: "DeletedAt",
                table: "CoordinatorProfiles");

            migrationBuilder.DropColumn(
                name: "DeletedBy",
                table: "CoordinatorProfiles");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "CoordinatorProfiles");
        }
    }
}
