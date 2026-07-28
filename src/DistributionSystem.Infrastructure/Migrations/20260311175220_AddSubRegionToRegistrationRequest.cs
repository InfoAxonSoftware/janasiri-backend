using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DistributionSystem.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSubRegionToRegistrationRequest : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "SubRegionId",
                table: "CustomerRegistrationRequests",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_CustomerRegistrationRequests_SubRegionId",
                table: "CustomerRegistrationRequests",
                column: "SubRegionId");

            migrationBuilder.AddForeignKey(
                name: "FK_CustomerRegistrationRequests_SubRegions_SubRegionId",
                table: "CustomerRegistrationRequests",
                column: "SubRegionId",
                principalTable: "SubRegions",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CustomerRegistrationRequests_SubRegions_SubRegionId",
                table: "CustomerRegistrationRequests");

            migrationBuilder.DropIndex(
                name: "IX_CustomerRegistrationRequests_SubRegionId",
                table: "CustomerRegistrationRequests");

            migrationBuilder.DropColumn(
                name: "SubRegionId",
                table: "CustomerRegistrationRequests");
        }
    }
}
