using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DistributionSystem.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddStockReports : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "StockReports",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RegionId = table.Column<Guid>(type: "uuid", nullable: false),
                    RegionName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CompanyName = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    ReportTitle = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    DateAsOf = table.Column<DateOnly>(type: "date", nullable: true),
                    ExportedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    OriginalFileName = table.Column<string>(type: "character varying(260)", maxLength: 260, nullable: true),
                    RowCount = table.Column<int>(type: "integer", nullable: false),
                    TotalOnHand = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    TotalAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    UploadedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UploadedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockReports", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StockReports_Regions_RegionId",
                        column: x => x.RegionId,
                        principalTable: "Regions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "StockReportRows",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    StockReportId = table.Column<Guid>(type: "uuid", nullable: false),
                    RowType = table.Column<int>(type: "integer", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    GroupName = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    Item = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    SalesDescription = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CostExVat = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    OnHand = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockReportRows", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StockReportRows_StockReports_StockReportId",
                        column: x => x.StockReportId,
                        principalTable: "StockReports",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StockReportRows_StockReportId",
                table: "StockReportRows",
                column: "StockReportId");

            migrationBuilder.CreateIndex(
                name: "IX_StockReportRows_StockReportId_SortOrder",
                table: "StockReportRows",
                columns: new[] { "StockReportId", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_StockReports_RegionId",
                table: "StockReports",
                column: "RegionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StockReports_UploadedAt",
                table: "StockReports",
                column: "UploadedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StockReportRows");

            migrationBuilder.DropTable(
                name: "StockReports");
        }
    }
}
