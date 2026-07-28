using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DistributionSystem.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddOutstandingReports : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OutstandingReports",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RegionId = table.Column<Guid>(type: "uuid", nullable: false),
                    RegionName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ReportDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UploadedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UploadedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutstandingReports", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OutstandingReports_Regions_RegionId",
                        column: x => x.RegionId,
                        principalTable: "Regions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "OutstandingEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OutstandingReportId = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerName = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    TxnType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    RefNo = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    TxnDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    AgeDays = table.Column<int>(type: "integer", nullable: true),
                    Current = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Bucket1_15 = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Bucket16_30 = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Bucket31_45 = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Above45 = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Balance = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    IsTotal = table.Column<bool>(type: "boolean", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutstandingEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OutstandingEntries_OutstandingReports_OutstandingReportId",
                        column: x => x.OutstandingReportId,
                        principalTable: "OutstandingReports",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OutstandingEntries_OutstandingReportId",
                table: "OutstandingEntries",
                column: "OutstandingReportId");

            migrationBuilder.CreateIndex(
                name: "IX_OutstandingEntries_OutstandingReportId_CustomerName",
                table: "OutstandingEntries",
                columns: new[] { "OutstandingReportId", "CustomerName" });

            migrationBuilder.CreateIndex(
                name: "IX_OutstandingReports_RegionId",
                table: "OutstandingReports",
                column: "RegionId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OutstandingEntries");

            migrationBuilder.DropTable(
                name: "OutstandingReports");
        }
    }
}
