using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DistributionSystem.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddRepRouteManyToMany : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Routes_SalesRepProfiles_RepId",
                table: "Routes");

            migrationBuilder.DropIndex(
                name: "IX_Routes_RepId",
                table: "Routes");

            migrationBuilder.DropColumn(
                name: "RepId",
                table: "Routes");

            migrationBuilder.CreateTable(
                name: "RepRoutes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RouteId = table.Column<Guid>(type: "uuid", nullable: false),
                    RepId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RepRoutes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RepRoutes_Routes_RouteId",
                        column: x => x.RouteId,
                        principalTable: "Routes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RepRoutes_SalesRepProfiles_RepId",
                        column: x => x.RepId,
                        principalTable: "SalesRepProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RepRoutes_RepId",
                table: "RepRoutes",
                column: "RepId");

            migrationBuilder.CreateIndex(
                name: "IX_RepRoutes_RouteId_RepId",
                table: "RepRoutes",
                columns: new[] { "RouteId", "RepId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RepRoutes");

            migrationBuilder.AddColumn<Guid>(
                name: "RepId",
                table: "Routes",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Routes_RepId",
                table: "Routes",
                column: "RepId");

            migrationBuilder.AddForeignKey(
                name: "FK_Routes_SalesRepProfiles_RepId",
                table: "Routes",
                column: "RepId",
                principalTable: "SalesRepProfiles",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }
    }
}
