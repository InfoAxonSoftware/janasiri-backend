using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DistributionSystem.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddUserRecordTrash : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "UserRecordTrashes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    RecordKind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    RecordId = table.Column<Guid>(type: "uuid", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    PurgedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserRecordTrashes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserRecordTrashes_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UserRecordTrashes_DeletedAt_PurgedAt",
                table: "UserRecordTrashes",
                columns: new[] { "DeletedAt", "PurgedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_UserRecordTrashes_UserId_RecordKind_RecordId",
                table: "UserRecordTrashes",
                columns: new[] { "UserId", "RecordKind", "RecordId" },
                unique: true);

            // Backfill the legacy shared coordinator flags into per-user entries.
            // Do not clear those flags: the migration is additive and preserves rollback history.
            // One legacy deletion affected all assigned coordinators, so that behavior is
            // conservatively preserved for rows deleted before this migration.
            migrationBuilder.Sql("""
                INSERT INTO "UserRecordTrashes" ("Id","UserId","RecordKind","RecordId","DeletedAt","PurgedAt")
                SELECT gen_random_uuid(), c."UserId", 'Order', o."Id",
                       COALESCE(o."CoordinatorDeletedAt", NOW()),
                       CASE WHEN o."IsPurgedByCoordinator"
                         THEN COALESCE(o."CoordinatorPurgedAt", NOW()) ELSE NULL END
                FROM "Orders" o
                JOIN "CustomerProfiles" cust ON cust."Id" = o."CustomerId"
                JOIN "CoordinatorProfiles" c ON c."Id" = cust."AssignedCoordinatorId"
                WHERE o."IsDeletedByCoordinator"
                ON CONFLICT ("UserId","RecordKind","RecordId") DO NOTHING;
            """);

            migrationBuilder.Sql("""
                INSERT INTO "UserRecordTrashes" ("Id","UserId","RecordKind","RecordId","DeletedAt","PurgedAt")
                SELECT gen_random_uuid(), c."UserId", 'Order', o."Id",
                       COALESCE(o."CoordinatorDeletedAt", NOW()),
                       CASE WHEN o."IsPurgedByCoordinator"
                         THEN COALESCE(o."CoordinatorPurgedAt", NOW()) ELSE NULL END
                FROM "Orders" o
                JOIN "RepCoordinators" rc ON rc."RepId" = o."RepId"
                JOIN "CoordinatorProfiles" c ON c."Id" = rc."CoordinatorId"
                WHERE o."IsDeletedByCoordinator"
                ON CONFLICT ("UserId","RecordKind","RecordId") DO NOTHING;
            """);

            migrationBuilder.Sql("""
                INSERT INTO "UserRecordTrashes" ("Id","UserId","RecordKind","RecordId","DeletedAt","PurgedAt")
                SELECT gen_random_uuid(), c."UserId", 'QuickRequest', r."Id",
                       COALESCE(r."CoordinatorDeletedAt", NOW()),
                       CASE WHEN r."IsPurgedByCoordinator"
                         THEN COALESCE(r."CoordinatorPurgedAt", NOW()) ELSE NULL END
                FROM "QuickRequests" r
                JOIN "RepCoordinators" rc ON rc."RepId" = r."RepId"
                JOIN "CoordinatorProfiles" c ON c."Id" = rc."CoordinatorId"
                WHERE r."IsDeletedByCoordinator"
                ON CONFLICT ("UserId","RecordKind","RecordId") DO NOTHING;
            """);

            migrationBuilder.Sql("""
                INSERT INTO "UserRecordTrashes" ("Id","UserId","RecordKind","RecordId","DeletedAt","PurgedAt")
                SELECT gen_random_uuid(), c."UserId", 'Quotation', q."Id",
                       COALESCE(q."CoordinatorDeletedAt", NOW()), NULL
                FROM "Quotations" q
                JOIN "CoordinatorProfiles" c ON c."Id" = q."CoordinatorId"
                WHERE q."IsDeletedByCoordinator"
                ON CONFLICT ("UserId","RecordKind","RecordId") DO NOTHING;
            """);

            migrationBuilder.Sql("""
                INSERT INTO "UserRecordTrashes" ("Id","UserId","RecordKind","RecordId","DeletedAt","PurgedAt")
                SELECT gen_random_uuid(), c."UserId", 'Quotation', q."Id",
                       COALESCE(q."CoordinatorDeletedAt", NOW()), NULL
                FROM "Quotations" q
                JOIN "RepCoordinators" rc ON rc."RepId" = q."RepId"
                JOIN "CoordinatorProfiles" c ON c."Id" = rc."CoordinatorId"
                WHERE q."IsDeletedByCoordinator"
                ON CONFLICT ("UserId","RecordKind","RecordId") DO NOTHING;
            """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UserRecordTrashes");
        }
    }
}
