-- Janasiri legacy Trash: READ-ONLY PRE-DEPLOYMENT DATA CHECK.
-- Run against a restored/staging snapshot FIRST. In production, use a read-only DB role.
-- This script makes NO changes. Do not run EF migrations before reviewing its counts.
BEGIN TRANSACTION READ ONLY;

SELECT current_database() AS database_name,
       current_schema() AS schema_name,
       current_setting('server_version') AS postgres_version,
       to_regprocedure('gen_random_uuid()') IS NOT NULL AS uuid_generator_available,
       to_regclass('"UserRecordTrashes"') IS NOT NULL AS personal_trash_table_already_exists;

SELECT "MigrationId"
FROM "__EFMigrationsHistory"
WHERE "MigrationId" LIKE '%AddUserRecordTrash%'
   OR "MigrationId" LIKE '%AddQuotationAdminPurgeAudit%'
   OR "MigrationId" LIKE '%AddRepPaymentAdminPurgeAudit%'
ORDER BY "MigrationId";

-- Flags are from the OLD schema only. Do not assume the three new migrations ran.
SELECT 'Orders' AS record_kind,
       COUNT(*) AS total,
       COUNT(*) FILTER (WHERE "IsDeleted") AS admin_deleted,
       COUNT(*) FILTER (WHERE "IsDeleted" AND "DeletedAt" IS NULL) AS admin_missing_deleted_at,
       COUNT(*) FILTER (WHERE "IsDeleted" AND "DeletedAt" < NOW() - INTERVAL '7 days') AS admin_would_expire_on_first_sweep,
       COUNT(*) FILTER (WHERE "IsDeletedByRep") AS rep_deleted,
       COUNT(*) FILTER (WHERE "IsPurgedByRep") AS rep_purged,
       COUNT(*) FILTER (WHERE "IsPurgedByRep" AND NOT "IsDeletedByRep") AS rep_purge_only,
       COUNT(*) FILTER (WHERE "IsDeletedByCoordinator") AS coordinator_deleted,
       COUNT(*) FILTER (WHERE "IsPurgedByCoordinator") AS coordinator_purged,
       COUNT(*) FILTER (WHERE "IsPurgedByCoordinator" AND NOT "IsDeletedByCoordinator") AS coordinator_purge_only,
       COUNT(*) FILTER (WHERE "IsDeletedByCustomer") AS customer_deleted
FROM "Orders";

SELECT 'QuickRequests' AS record_kind,
       COUNT(*) AS total,
       COUNT(*) FILTER (WHERE "IsDeletedByAdmin") AS admin_deleted,
       COUNT(*) FILTER (WHERE "IsDeletedByAdmin" AND "AdminDeletedAt" IS NULL) AS admin_missing_deleted_at,
       COUNT(*) FILTER (WHERE "IsDeletedByAdmin" AND "AdminDeletedAt" < NOW() - INTERVAL '7 days') AS admin_would_expire_on_first_sweep,
       COUNT(*) FILTER (WHERE "IsDeletedByRep") AS rep_deleted,
       COUNT(*) FILTER (WHERE "IsPurgedByRep") AS rep_purged,
       COUNT(*) FILTER (WHERE "IsPurgedByRep" AND NOT "IsDeletedByRep") AS rep_purge_only,
       COUNT(*) FILTER (WHERE "IsDeletedByCoordinator") AS coordinator_deleted,
       COUNT(*) FILTER (WHERE "IsPurgedByCoordinator") AS coordinator_purged,
       COUNT(*) FILTER (WHERE "IsPurgedByCoordinator" AND NOT "IsDeletedByCoordinator") AS coordinator_purge_only
FROM "QuickRequests";

SELECT 'Quotations' AS record_kind,
       COUNT(*) AS total,
       COUNT(*) FILTER (WHERE "IsDeleted") AS admin_deleted,
       COUNT(*) FILTER (WHERE "IsDeleted" AND "DeletedAt" IS NULL) AS admin_missing_deleted_at,
       COUNT(*) FILTER (WHERE "IsDeleted" AND "DeletedAt" < NOW() - INTERVAL '7 days') AS admin_would_expire_on_first_sweep,
       COUNT(*) FILTER (WHERE "IsDeletedByRep") AS rep_deleted,
       COUNT(*) FILTER (WHERE "IsDeletedByCoordinator") AS coordinator_deleted,
       COUNT(*) FILTER (WHERE "IsDeletedByCustomer") AS customer_deleted
FROM "Quotations";

SELECT 'RepPayments' AS record_kind,
       COUNT(*) AS total,
       COUNT(*) FILTER (WHERE "IsDeletedByAdmin") AS admin_deleted,
       COUNT(*) FILTER (WHERE "IsDeletedByAdmin" AND "AdminDeletedAt" IS NULL) AS admin_missing_deleted_at,
       COUNT(*) FILTER (WHERE "IsDeletedByAdmin" AND "AdminDeletedAt" < NOW() - INTERVAL '7 days') AS admin_would_expire_on_first_sweep,
       COUNT(*) FILTER (WHERE "IsDeletedBySalesRep") AS rep_deleted,
       COUNT(*) FILTER (WHERE "IsDeletedByCoordinator") AS coordinator_deleted
FROM "RepPayments";

-- ALL unmappable deleted records are deployment blockers, not safe to ignore.
SELECT 'Orders: coordinator' AS blocker, COUNT(*) AS rows_affected
FROM "Orders" o
WHERE (o."IsDeletedByCoordinator" OR o."IsPurgedByCoordinator")
  AND NOT EXISTS (
    SELECT 1 FROM "CustomerProfiles" cust
    JOIN "CoordinatorProfiles" c ON c."Id" = cust."AssignedCoordinatorId"
    WHERE cust."Id" = o."CustomerId")
  AND NOT EXISTS (
    SELECT 1 FROM "RepCoordinators" rc
    JOIN "CoordinatorProfiles" c ON c."Id" = rc."CoordinatorId"
    WHERE rc."RepId" = o."RepId")
UNION ALL
SELECT 'QuickRequests: coordinator', COUNT(*)
FROM "QuickRequests" r
WHERE (r."IsDeletedByCoordinator" OR r."IsPurgedByCoordinator")
  AND NOT EXISTS (
    SELECT 1 FROM "RepCoordinators" rc
    JOIN "CoordinatorProfiles" c ON c."Id" = rc."CoordinatorId"
    WHERE rc."RepId" = r."RepId")
UNION ALL
SELECT 'Quotations: coordinator', COUNT(*)
FROM "Quotations" q
WHERE q."IsDeletedByCoordinator"
  AND NOT EXISTS (SELECT 1 FROM "CoordinatorProfiles" c WHERE c."Id" = q."CoordinatorId")
  AND NOT EXISTS (
    SELECT 1 FROM "RepCoordinators" rc
    JOIN "CoordinatorProfiles" c ON c."Id" = rc."CoordinatorId"
    WHERE rc."RepId" = q."RepId")
UNION ALL
SELECT 'RepPayments: coordinator', COUNT(*)
FROM "RepPayments" p
WHERE p."IsDeletedByCoordinator"
  AND NOT EXISTS (
    SELECT 1 FROM "RepCoordinators" rc
    JOIN "CoordinatorProfiles" c ON c."Id" = rc."CoordinatorId"
    WHERE rc."RepId" = p."RepId")
UNION ALL
SELECT 'Orders: rep', COUNT(*) FROM "Orders" o
WHERE (o."IsDeletedByRep" OR o."IsPurgedByRep")
  AND NOT EXISTS (SELECT 1 FROM "SalesRepProfiles" rp WHERE rp."Id" = o."RepId")
UNION ALL
SELECT 'QuickRequests: rep', COUNT(*) FROM "QuickRequests" r
WHERE (r."IsDeletedByRep" OR r."IsPurgedByRep")
  AND NOT EXISTS (SELECT 1 FROM "SalesRepProfiles" rp WHERE rp."Id" = r."RepId")
UNION ALL
SELECT 'Quotations: rep', COUNT(*) FROM "Quotations" q
WHERE q."IsDeletedByRep"
  AND NOT EXISTS (SELECT 1 FROM "SalesRepProfiles" rp WHERE rp."Id" = q."RepId")
UNION ALL
SELECT 'RepPayments: rep', COUNT(*) FROM "RepPayments" p
WHERE p."IsDeletedBySalesRep"
  AND NOT EXISTS (SELECT 1 FROM "SalesRepProfiles" rp WHERE rp."Id" = p."RepId")
UNION ALL
SELECT 'Orders: customer', COUNT(*) FROM "Orders" o
WHERE o."IsDeletedByCustomer"
  AND NOT EXISTS (SELECT 1 FROM "CustomerProfiles" c WHERE c."Id" = o."CustomerId")
UNION ALL
SELECT 'Quotations: customer', COUNT(*) FROM "Quotations" q
WHERE q."IsDeletedByCustomer"
  AND NOT EXISTS (SELECT 1 FROM "CustomerProfiles" c WHERE c."Id" = q."CustomerId");

ROLLBACK;

-- GO/NO-GO:
-- Every rows_affected must be 0. Fix inconsistencies in a COPY, then re-run.
-- Review admin_would_expire_on_first_sweep before production: these old
-- records will leave Admin Trash during the next hourly cleanup; their
-- business and payment rows are still retained for audit.
-- Migration uses NOW() only for old Admin-deleted rows with NULL dates,
-- granting them a new seven-day window. Never overwrite valid timestamps.
-- Back up DB and evidence storage and pause legacy writes while the
-- migration and new API version roll out together; rollback after writes
-- requires a data-reconciliation plan, not simply dropping the new table.
