using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DistributionSystem.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RepMultiRegionSubRegionCoordinator : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SalesRepProfiles_CoordinatorProfiles_CoordinatorId",
                table: "SalesRepProfiles");

            migrationBuilder.DropForeignKey(
                name: "FK_SalesRepProfiles_Regions_RegionId",
                table: "SalesRepProfiles");

            migrationBuilder.DropForeignKey(
                name: "FK_SalesRepProfiles_SubRegions_SubRegionId",
                table: "SalesRepProfiles");

            migrationBuilder.DropIndex(
                name: "IX_SalesRepProfiles_CoordinatorId",
                table: "SalesRepProfiles");

            migrationBuilder.DropIndex(
                name: "IX_SalesRepProfiles_RegionId",
                table: "SalesRepProfiles");

            migrationBuilder.DropIndex(
                name: "IX_SalesRepProfiles_SubRegionId",
                table: "SalesRepProfiles");

            migrationBuilder.DropColumn(
                name: "CoordinatorId",
                table: "SalesRepProfiles");

            migrationBuilder.DropColumn(
                name: "RegionId",
                table: "SalesRepProfiles");

            migrationBuilder.DropColumn(
                name: "SubRegionId",
                table: "SalesRepProfiles");

            migrationBuilder.CreateTable(
                name: "RepCoordinators",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RepId = table.Column<Guid>(type: "uuid", nullable: false),
                    CoordinatorId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RepCoordinators", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RepCoordinators_CoordinatorProfiles_CoordinatorId",
                        column: x => x.CoordinatorId,
                        principalTable: "CoordinatorProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RepCoordinators_SalesRepProfiles_RepId",
                        column: x => x.RepId,
                        principalTable: "SalesRepProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RepRegions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RepId = table.Column<Guid>(type: "uuid", nullable: false),
                    RegionId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RepRegions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RepRegions_Regions_RegionId",
                        column: x => x.RegionId,
                        principalTable: "Regions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RepRegions_SalesRepProfiles_RepId",
                        column: x => x.RepId,
                        principalTable: "SalesRepProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RepSubRegions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RepId = table.Column<Guid>(type: "uuid", nullable: false),
                    SubRegionId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RepSubRegions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RepSubRegions_SalesRepProfiles_RepId",
                        column: x => x.RepId,
                        principalTable: "SalesRepProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RepSubRegions_SubRegions_SubRegionId",
                        column: x => x.SubRegionId,
                        principalTable: "SubRegions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RepCoordinators_CoordinatorId",
                table: "RepCoordinators",
                column: "CoordinatorId");

            migrationBuilder.CreateIndex(
                name: "IX_RepCoordinators_RepId_CoordinatorId",
                table: "RepCoordinators",
                columns: new[] { "RepId", "CoordinatorId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RepRegions_RegionId",
                table: "RepRegions",
                column: "RegionId");

            migrationBuilder.CreateIndex(
                name: "IX_RepRegions_RepId_RegionId",
                table: "RepRegions",
                columns: new[] { "RepId", "RegionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RepSubRegions_RepId_SubRegionId",
                table: "RepSubRegions",
                columns: new[] { "RepId", "SubRegionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RepSubRegions_SubRegionId",
                table: "RepSubRegions",
                column: "SubRegionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RepCoordinators");

            migrationBuilder.DropTable(
                name: "RepRegions");

            migrationBuilder.DropTable(
                name: "RepSubRegions");

            migrationBuilder.AddColumn<Guid>(
                name: "CoordinatorId",
                table: "SalesRepProfiles",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RegionId",
                table: "SalesRepProfiles",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SubRegionId",
                table: "SalesRepProfiles",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_SalesRepProfiles_CoordinatorId",
                table: "SalesRepProfiles",
                column: "CoordinatorId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesRepProfiles_RegionId",
                table: "SalesRepProfiles",
                column: "RegionId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesRepProfiles_SubRegionId",
                table: "SalesRepProfiles",
                column: "SubRegionId");

            migrationBuilder.AddForeignKey(
                name: "FK_SalesRepProfiles_CoordinatorProfiles_CoordinatorId",
                table: "SalesRepProfiles",
                column: "CoordinatorId",
                principalTable: "CoordinatorProfiles",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_SalesRepProfiles_Regions_RegionId",
                table: "SalesRepProfiles",
                column: "RegionId",
                principalTable: "Regions",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_SalesRepProfiles_SubRegions_SubRegionId",
                table: "SalesRepProfiles",
                column: "SubRegionId",
                principalTable: "SubRegions",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }
    }
}
