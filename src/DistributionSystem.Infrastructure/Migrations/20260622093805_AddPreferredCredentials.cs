using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DistributionSystem.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPreferredCredentials : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PreferredPassword",
                table: "CustomerRegistrationRequests",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PreferredUsername",
                table: "CustomerRegistrationRequests",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PreferredPassword",
                table: "CustomerRegistrationRequests");

            migrationBuilder.DropColumn(
                name: "PreferredUsername",
                table: "CustomerRegistrationRequests");
        }
    }
}
