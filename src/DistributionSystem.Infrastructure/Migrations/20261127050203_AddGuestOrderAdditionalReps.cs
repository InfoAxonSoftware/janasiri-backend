using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DistributionSystem.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddGuestOrderAdditionalReps : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "AssignmentVersion",
                table: "QuickRequests",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "GuestOrderAdditionalReps",
                columns: table => new
                {
                    QuickRequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    RepId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GuestOrderAdditionalReps", x => new { x.QuickRequestId, x.RepId });
                    table.ForeignKey(
                        name: "FK_GuestOrderAdditionalReps_QuickRequests_QuickRequestId",
                        column: x => x.QuickRequestId,
                        principalTable: "QuickRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_GuestOrderAdditionalReps_SalesRepProfiles_RepId",
                        column: x => x.RepId,
                        principalTable: "SalesRepProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GuestOrderAdditionalReps_RepId",
                table: "GuestOrderAdditionalReps",
                column: "RepId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GuestOrderAdditionalReps");

            migrationBuilder.DropColumn(
                name: "AssignmentVersion",
                table: "QuickRequests");
        }
    }
}
