using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DistributionSystem.Infrastructure.src.DistributionSystem.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTokenVersionPasswordChangedAtToUser : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "PasswordChangedAt",
                table: "Users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TokenVersion",
                table: "Users",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "ContactEmail",
                table: "Complaints",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ContactName",
                table: "Complaints",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ContactPhone",
                table: "Complaints",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ContactPosition",
                table: "Complaints",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PasswordChangedAt",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "TokenVersion",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "ContactEmail",
                table: "Complaints");

            migrationBuilder.DropColumn(
                name: "ContactName",
                table: "Complaints");

            migrationBuilder.DropColumn(
                name: "ContactPhone",
                table: "Complaints");

            migrationBuilder.DropColumn(
                name: "ContactPosition",
                table: "Complaints");
        }
    }
}
