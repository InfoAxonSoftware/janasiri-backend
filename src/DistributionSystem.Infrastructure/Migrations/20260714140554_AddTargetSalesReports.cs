using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DistributionSystem.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTargetSalesReports : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TargetSalesReports",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TargetId = table.Column<Guid>(type: "uuid", nullable: false),
                    RepId = table.Column<Guid>(type: "uuid", nullable: false),
                    OriginalFileName = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    FromDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AsAtDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ActualSales = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    IsCurrent = table.Column<bool>(type: "boolean", nullable: false),
                    UploadedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UploadedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    UploadedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TargetSalesReports", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TargetSalesReports_SalesTargets_TargetId",
                        column: x => x.TargetId,
                        principalTable: "SalesTargets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TargetSalesReportEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TargetSalesReportId = table.Column<Guid>(type: "uuid", nullable: false),
                    TxnDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RefNo = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    CustomerName = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    ItemDescription = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    Qty = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Discount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    SalesWithTax = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TargetSalesReportEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TargetSalesReportEntries_TargetSalesReports_TargetSalesRepo~",
                        column: x => x.TargetSalesReportId,
                        principalTable: "TargetSalesReports",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TargetSalesReportEntries_TargetSalesReportId",
                table: "TargetSalesReportEntries",
                column: "TargetSalesReportId");

            migrationBuilder.CreateIndex(
                name: "IX_TargetSalesReports_RepId",
                table: "TargetSalesReports",
                column: "RepId");

            migrationBuilder.CreateIndex(
                name: "IX_TargetSalesReports_TargetId_IsCurrent",
                table: "TargetSalesReports",
                columns: new[] { "TargetId", "IsCurrent" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TargetSalesReportEntries");

            migrationBuilder.DropTable(
                name: "TargetSalesReports");
        }
    }
}
