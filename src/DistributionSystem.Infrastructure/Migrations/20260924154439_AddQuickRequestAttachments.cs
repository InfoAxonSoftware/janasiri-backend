using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DistributionSystem.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddQuickRequestAttachments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "QuickRequestAttachments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    QuickRequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    StorageKey = table.Column<string>(type: "text", nullable: false),
                    OriginalFileName = table.Column<string>(type: "text", nullable: false),
                    ContentType = table.Column<string>(type: "text", nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    UploadedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QuickRequestAttachments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QuickRequestAttachments_QuickRequests_QuickRequestId",
                        column: x => x.QuickRequestId,
                        principalTable: "QuickRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_QuickRequestAttachments_QuickRequestId",
                table: "QuickRequestAttachments",
                column: "QuickRequestId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "QuickRequestAttachments");
        }
    }
}
