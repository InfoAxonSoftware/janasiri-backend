using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DistributionSystem.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class MakeQuickRequestRepNullable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_QuickRequests_SalesRepProfiles_RepId",
                table: "QuickRequests");

            migrationBuilder.AlterColumn<Guid>(
                name: "RepId",
                table: "QuickRequests",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddForeignKey(
                name: "FK_QuickRequests_SalesRepProfiles_RepId",
                table: "QuickRequests",
                column: "RepId",
                principalTable: "SalesRepProfiles",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_QuickRequests_SalesRepProfiles_RepId",
                table: "QuickRequests");

            migrationBuilder.AlterColumn<Guid>(
                name: "RepId",
                table: "QuickRequests",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AddForeignKey(
                name: "FK_QuickRequests_SalesRepProfiles_RepId",
                table: "QuickRequests",
                column: "RepId",
                principalTable: "SalesRepProfiles",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
