using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DistributionSystem.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSalesSummaryReports : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SalesSummaryReports",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RegionId = table.Column<Guid>(type: "uuid", nullable: false),
                    RegionName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    PeriodFrom = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    PeriodTo = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    OriginalFileName = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    UploadedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UploadedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SalesSummaryReports", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SalesSummaryReports_Regions_RegionId",
                        column: x => x.RegionId,
                        principalTable: "Regions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SalesSummaryEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SalesSummaryReportId = table.Column<Guid>(type: "uuid", nullable: false),
                    GroupName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    SalesWithTax = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Tax = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    NetSales = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Discount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    GrossSales = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    IsTotal = table.Column<bool>(type: "boolean", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SalesSummaryEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SalesSummaryEntries_SalesSummaryReports_SalesSummaryReportId",
                        column: x => x.SalesSummaryReportId,
                        principalTable: "SalesSummaryReports",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SalesSummaryEntries_SalesSummaryReportId",
                table: "SalesSummaryEntries",
                column: "SalesSummaryReportId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesSummaryReports_RegionId_PeriodFrom_PeriodTo",
                table: "SalesSummaryReports",
                columns: new[] { "RegionId", "PeriodFrom", "PeriodTo" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SalesSummaryEntries");

            migrationBuilder.DropTable(
                name: "SalesSummaryReports");
        }
    }
}
