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
            // Fail closed: if a legacy private deletion has no resolvable user, do not
            // silently migrate and make it visible to a future role assignment.
            // Resolve these using the read-only production preflight before deploy.
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1 FROM "Orders" o
                        WHERE (o."IsDeletedByCoordinator" OR o."IsPurgedByCoordinator")
                          AND NOT EXISTS (
                            SELECT 1 FROM "CustomerProfiles" cust
                            JOIN "CoordinatorProfiles" c ON c."Id" = cust."AssignedCoordinatorId"
                            WHERE cust."Id" = o."CustomerId")
                          AND NOT EXISTS (
                            SELECT 1 FROM "RepCoordinators" rc
                            JOIN "CoordinatorProfiles" c ON c."Id" = rc."CoordinatorId"
                            WHERE rc."RepId" = o."RepId")
                    ) THEN
                        RAISE EXCEPTION 'Trash migration blocked: legacy coordinator Orders with no resolvable coordinator';
                    END IF;

                    IF EXISTS (
                        SELECT 1 FROM "QuickRequests" r
                        WHERE (r."IsDeletedByCoordinator" OR r."IsPurgedByCoordinator")
                          AND NOT EXISTS (
                            SELECT 1 FROM "RepCoordinators" rc
                            JOIN "CoordinatorProfiles" c ON c."Id" = rc."CoordinatorId"
                            WHERE rc."RepId" = r."RepId")
                    ) THEN
                        RAISE EXCEPTION 'Trash migration blocked: legacy coordinator QuickRequests with no resolvable coordinator';
                    END IF;

                    IF EXISTS (
                        SELECT 1 FROM "Quotations" q
                        WHERE q."IsDeletedByCoordinator"
                          AND NOT EXISTS (
                            SELECT 1 FROM "CoordinatorProfiles" c WHERE c."Id" = q."CoordinatorId")
                          AND NOT EXISTS (
                            SELECT 1 FROM "RepCoordinators" rc
                            JOIN "CoordinatorProfiles" c ON c."Id" = rc."CoordinatorId"
                            WHERE rc."RepId" = q."RepId")
                    ) THEN
                        RAISE EXCEPTION 'Trash migration blocked: legacy coordinator Quotations with no resolvable coordinator';
                    END IF;

                    IF EXISTS (
                        SELECT 1 FROM "RepPayments" p
                        WHERE p."IsDeletedByCoordinator"
                          AND NOT EXISTS (
                            SELECT 1 FROM "RepCoordinators" rc
                            JOIN "CoordinatorProfiles" c ON c."Id" = rc."CoordinatorId"
                            WHERE rc."RepId" = p."RepId")
                    ) THEN
                        RAISE EXCEPTION 'Trash migration blocked: legacy coordinator Payment Reports with no resolvable coordinator';
                    END IF;

                    IF EXISTS (
                        SELECT 1 FROM "Orders" o
                        WHERE (o."IsDeletedByRep" OR o."IsPurgedByRep")
                          AND NOT EXISTS (SELECT 1 FROM "SalesRepProfiles" rep WHERE rep."Id" = o."RepId"))
                       OR EXISTS (
                        SELECT 1 FROM "QuickRequests" r
                        WHERE (r."IsDeletedByRep" OR r."IsPurgedByRep")
                          AND NOT EXISTS (SELECT 1 FROM "SalesRepProfiles" rep WHERE rep."Id" = r."RepId"))
                       OR EXISTS (
                        SELECT 1 FROM "Quotations" q
                        WHERE q."IsDeletedByRep"
                          AND NOT EXISTS (SELECT 1 FROM "SalesRepProfiles" rep WHERE rep."Id" = q."RepId"))
                       OR EXISTS (
                        SELECT 1 FROM "RepPayments" p
                        WHERE p."IsDeletedBySalesRep"
                          AND NOT EXISTS (SELECT 1 FROM "SalesRepProfiles" rep WHERE rep."Id" = p."RepId"))
                    THEN
                        RAISE EXCEPTION 'Trash migration blocked: legacy Sales Rep deletion has no matching profile';
                    END IF;

                    IF EXISTS (
                        SELECT 1 FROM "Orders" o
                        WHERE o."IsDeletedByCustomer"
                          AND NOT EXISTS (SELECT 1 FROM "CustomerProfiles" c WHERE c."Id" = o."CustomerId"))
                       OR EXISTS (
                        SELECT 1 FROM "Quotations" q
                        WHERE q."IsDeletedByCustomer"
                          AND NOT EXISTS (SELECT 1 FROM "CustomerProfiles" c WHERE c."Id" = q."CustomerId"))
                    THEN
                        RAISE EXCEPTION 'Trash migration blocked: legacy Customer deletion has no matching profile';
                    END IF;
                END $$;
            """);

            // Old Admin deletions occasionally have missing timestamps. Grant a
            // conservative fresh seven-day Trash window rather than leave these
            // records in Trash forever or purge immediately with a guessed date.
            // Records with real deletion dates are NEVER overwritten.
            migrationBuilder.Sql("""
                UPDATE "Orders" SET "DeletedAt" = NOW()
                WHERE "IsDeleted" AND "DeletedAt" IS NULL;
                UPDATE "QuickRequests" SET "AdminDeletedAt" = NOW()
                WHERE "IsDeletedByAdmin" AND "AdminDeletedAt" IS NULL;
                UPDATE "Quotations" SET "DeletedAt" = NOW()
                WHERE "IsDeleted" AND "DeletedAt" IS NULL;
                UPDATE "RepPayments" SET "AdminDeletedAt" = NOW()
                WHERE "IsDeletedByAdmin" AND "AdminDeletedAt" IS NULL;
            """);

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
                WHERE o."IsDeletedByCoordinator" OR o."IsPurgedByCoordinator"
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
                WHERE o."IsDeletedByCoordinator" OR o."IsPurgedByCoordinator"
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
                WHERE r."IsDeletedByCoordinator" OR r."IsPurgedByCoordinator"
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

            // Backfill Sales Rep personal deletions. Legacy purge flags remain represented
            // as PurgedAt so already-purged entries do not return to Trash.
            migrationBuilder.Sql("""
                INSERT INTO "UserRecordTrashes" ("Id","UserId","RecordKind","RecordId","DeletedAt","PurgedAt")
                SELECT gen_random_uuid(), rep."UserId", 'Order', o."Id",
                       COALESCE(o."RepDeletedAt", NOW()),
                       CASE WHEN o."IsPurgedByRep" THEN COALESCE(o."RepPurgedAt", NOW()) ELSE NULL END
                FROM "Orders" o
                JOIN "SalesRepProfiles" rep ON rep."Id" = o."RepId"
                WHERE o."IsDeletedByRep" OR o."IsPurgedByRep"
                ON CONFLICT ("UserId","RecordKind","RecordId") DO NOTHING;
            """);

            migrationBuilder.Sql("""
                INSERT INTO "UserRecordTrashes" ("Id","UserId","RecordKind","RecordId","DeletedAt","PurgedAt")
                SELECT gen_random_uuid(), rep."UserId", 'QuickRequest', r."Id",
                       COALESCE(r."RepDeletedAt", NOW()),
                       CASE WHEN r."IsPurgedByRep" THEN COALESCE(r."RepPurgedAt", NOW()) ELSE NULL END
                FROM "QuickRequests" r
                JOIN "SalesRepProfiles" rep ON rep."Id" = r."RepId"
                WHERE r."IsDeletedByRep" OR r."IsPurgedByRep"
                ON CONFLICT ("UserId","RecordKind","RecordId") DO NOTHING;
            """);

            migrationBuilder.Sql("""
                INSERT INTO "UserRecordTrashes" ("Id","UserId","RecordKind","RecordId","DeletedAt","PurgedAt")
                SELECT gen_random_uuid(), rep."UserId", 'Quotation', q."Id",
                       COALESCE(q."RepDeletedAt", NOW()), NULL
                FROM "Quotations" q
                JOIN "SalesRepProfiles" rep ON rep."Id" = q."RepId"
                WHERE q."IsDeletedByRep"
                ON CONFLICT ("UserId","RecordKind","RecordId") DO NOTHING;
            """);

            // Customer deletion is private; do not modify shared Order/Quotation fields.
            migrationBuilder.Sql("""
                INSERT INTO "UserRecordTrashes" ("Id","UserId","RecordKind","RecordId","DeletedAt","PurgedAt")
                SELECT gen_random_uuid(), customer."UserId", 'Order', o."Id",
                       COALESCE(o."CustomerDeletedAt", NOW()), NULL
                FROM "Orders" o
                JOIN "CustomerProfiles" customer ON customer."Id" = o."CustomerId"
                WHERE o."IsDeletedByCustomer"
                ON CONFLICT ("UserId","RecordKind","RecordId") DO NOTHING;
            """);

            migrationBuilder.Sql("""
                INSERT INTO "UserRecordTrashes" ("Id","UserId","RecordKind","RecordId","DeletedAt","PurgedAt")
                SELECT gen_random_uuid(), customer."UserId", 'Quotation', q."Id",
                       COALESCE(q."CustomerDeletedAt", NOW()), NULL
                FROM "Quotations" q
                JOIN "CustomerProfiles" customer ON customer."Id" = q."CustomerId"
                WHERE q."IsDeletedByCustomer"
                ON CONFLICT ("UserId","RecordKind","RecordId") DO NOTHING;
            """);

            // Payment reports also use private per-user Trash. Preserve legacy Rep/Coordinator deletions.
            migrationBuilder.Sql("""
                INSERT INTO "UserRecordTrashes" ("Id","UserId","RecordKind","RecordId","DeletedAt","PurgedAt")
                SELECT gen_random_uuid(), rep."UserId", 'RepPayment', p."Id",
                       COALESCE(p."SalesRepDeletedAt", NOW()), NULL
                FROM "RepPayments" p
                JOIN "SalesRepProfiles" rep ON rep."Id" = p."RepId"
                WHERE p."IsDeletedBySalesRep"
                ON CONFLICT ("UserId","RecordKind","RecordId") DO NOTHING;
            """);

            migrationBuilder.Sql("""
                INSERT INTO "UserRecordTrashes" ("Id","UserId","RecordKind","RecordId","DeletedAt","PurgedAt")
                SELECT gen_random_uuid(), coordinator."UserId", 'RepPayment', p."Id",
                       COALESCE(p."CoordinatorDeletedAt", NOW()), NULL
                FROM "RepPayments" p
                JOIN "RepCoordinators" rc ON rc."RepId" = p."RepId"
                JOIN "CoordinatorProfiles" coordinator ON coordinator."Id" = rc."CoordinatorId"
                WHERE p."IsDeletedByCoordinator"
                ON CONFLICT ("UserId","RecordKind","RecordId") DO NOTHING;
            """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // New private deletions are not represented by the legacy role-wide
            // booleans. Dropping populated markers can re-expose private records.
            // Block rollback until a reviewed reconciliation has run.
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM "UserRecordTrashes") THEN
                        RAISE EXCEPTION 'Unsafe Trash rollback: UserRecordTrashes contains private deletion state; reconcile before downgrading';
                    END IF;
                END $$;
            """);

            migrationBuilder.DropTable(
                name: "UserRecordTrashes");
        }
    }
}
