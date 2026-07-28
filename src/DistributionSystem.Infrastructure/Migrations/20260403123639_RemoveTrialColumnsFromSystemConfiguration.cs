using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DistributionSystem.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RemoveTrialColumnsFromSystemConfiguration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsTrialRunning",
                table: "SystemConfigurations");

            migrationBuilder.DropColumn(
                name: "TrialEndsAtUtc",
                table: "SystemConfigurations");

            migrationBuilder.DropColumn(
                name: "TrialRemainingSeconds",
                table: "SystemConfigurations");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsTrialRunning",
                table: "SystemConfigurations",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "TrialEndsAtUtc",
                table: "SystemConfigurations",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TrialRemainingSeconds",
                table: "SystemConfigurations",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }
    }
}
