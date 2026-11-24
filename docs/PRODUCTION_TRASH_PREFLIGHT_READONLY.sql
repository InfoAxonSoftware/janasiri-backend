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

-- Personal Trash already older than seven days will leave each user's
-- visible Trash on the first enabled cleanup sweep, but business/financial
-- rows remain stored and permanently hidden from that user.
-- Review these counts BEFORE enabling TRASH_CLEANUP_ENABLED.
WITH legacy_personal AS (
    SELECT 'Order'::text AS record_kind, 'SalesRep'::text AS role_name,
           "RepDeletedAt" AS deleted_at, "IsPurgedByRep" AS already_purged
    FROM "Orders" WHERE "IsDeletedByRep" OR "IsPurgedByRep"
    UNION ALL
    SELECT 'Order', 'Coordinator', "CoordinatorDeletedAt", "IsPurgedByCoordinator"
    FROM "Orders" WHERE "IsDeletedByCoordinator" OR "IsPurgedByCoordinator"
    UNION ALL
    SELECT 'Order', 'Customer', "CustomerDeletedAt", false
    FROM "Orders" WHERE "IsDeletedByCustomer"
    UNION ALL
    SELECT 'QuickRequest', 'SalesRep', "RepDeletedAt", "IsPurgedByRep"
    FROM "QuickRequests" WHERE "IsDeletedByRep" OR "IsPurgedByRep"
    UNION ALL
    SELECT 'QuickRequest', 'Coordinator', "CoordinatorDeletedAt", "IsPurgedByCoordinator"
    FROM "QuickRequests" WHERE "IsDeletedByCoordinator" OR "IsPurgedByCoordinator"
    UNION ALL
    SELECT 'Quotation', 'SalesRep', "RepDeletedAt", false
    FROM "Quotations" WHERE "IsDeletedByRep"
    UNION ALL
    SELECT 'Quotation', 'Coordinator', "CoordinatorDeletedAt", false
    FROM "Quotations" WHERE "IsDeletedByCoordinator"
    UNION ALL
    SELECT 'Quotation', 'Customer', "CustomerDeletedAt", false
    FROM "Quotations" WHERE "IsDeletedByCustomer"
    UNION ALL
    SELECT 'RepPayment', 'SalesRep', "SalesRepDeletedAt", false
    FROM "RepPayments" WHERE "IsDeletedBySalesRep"
    UNION ALL
    SELECT 'RepPayment', 'Coordinator', "CoordinatorDeletedAt", false
    FROM "RepPayments" WHERE "IsDeletedByCoordinator"
)
SELECT record_kind, role_name,
       COUNT(*) AS legacy_personal_hidden,
       COUNT(*) FILTER (WHERE already_purged) AS already_permanent,
       COUNT(*) FILTER (WHERE NOT already_purged AND deleted_at IS NULL) AS unknown_deletion_date_fresh_window,
       COUNT(*) FILTER (WHERE NOT already_purged AND deleted_at < NOW() - INTERVAL '7 days') AS would_expire_on_first_sweep
FROM legacy_personal
GROUP BY record_kind, role_name ORDER BY record_kind, role_name;

-- Legacy role-wide Coordinator deletes do not identify the actor.
-- Count the CURRENT Coordinator recipients each affected record would receive.
-- A value >1 is not automatically an error: it demands human review because
-- multiple private Trash markers will replace one historic shared flag.
WITH legacy AS (
    SELECT 'Order'::text AS kind, o."Id" AS record_id
    FROM "Orders" o
    WHERE o."IsDeletedByCoordinator" OR o."IsPurgedByCoordinator"
    UNION ALL
    SELECT 'QuickRequest', q."Id"
    FROM "QuickRequests" q
    WHERE q."IsDeletedByCoordinator" OR q."IsPurgedByCoordinator"
    UNION ALL
    SELECT 'Quotation', q."Id"
    FROM "Quotations" q
    WHERE q."IsDeletedByCoordinator"
    UNION ALL
    SELECT 'RepPayment', p."Id"
    FROM "RepPayments" p
    WHERE p."IsDeletedByCoordinator"
),
current_recipients AS (
    SELECT 'Order'::text AS kind, o."Id" AS record_id, c."UserId" AS user_id
    FROM "Orders" o
    JOIN "CustomerProfiles" cp ON cp."Id" = o."CustomerId"
    JOIN "CoordinatorProfiles" c ON c."Id" = cp."AssignedCoordinatorId"
    WHERE o."IsDeletedByCoordinator" OR o."IsPurgedByCoordinator"
    UNION ALL
    SELECT 'Order', o."Id", c."UserId"
    FROM "Orders" o
    JOIN "RepCoordinators" rc ON rc."RepId" = o."RepId"
    JOIN "CoordinatorProfiles" c ON c."Id" = rc."CoordinatorId"
    WHERE o."IsDeletedByCoordinator" OR o."IsPurgedByCoordinator"
    UNION ALL
    SELECT 'QuickRequest', q."Id", c."UserId"
    FROM "QuickRequests" q
    JOIN "RepCoordinators" rc ON rc."RepId" = q."RepId"
    JOIN "CoordinatorProfiles" c ON c."Id" = rc."CoordinatorId"
    WHERE q."IsDeletedByCoordinator" OR q."IsPurgedByCoordinator"
    UNION ALL
    SELECT 'Quotation', q."Id", c."UserId"
    FROM "Quotations" q
    JOIN "CoordinatorProfiles" c ON c."Id" = q."CoordinatorId"
    WHERE q."IsDeletedByCoordinator"
    UNION ALL
    SELECT 'Quotation', q."Id", c."UserId"
    FROM "Quotations" q
    JOIN "RepCoordinators" rc ON rc."RepId" = q."RepId"
    JOIN "CoordinatorProfiles" c ON c."Id" = rc."CoordinatorId"
    WHERE q."IsDeletedByCoordinator"
    UNION ALL
    SELECT 'RepPayment', p."Id", c."UserId"
    FROM "RepPayments" p
    JOIN "RepCoordinators" rc ON rc."RepId" = p."RepId"
    JOIN "CoordinatorProfiles" c ON c."Id" = rc."CoordinatorId"
    WHERE p."IsDeletedByCoordinator"
),
recipient_counts AS (
    SELECT kind, record_id, COUNT(DISTINCT user_id) AS recipient_count
    FROM current_recipients GROUP BY kind, record_id
)
SELECT l.kind AS record_kind,
       COUNT(*) AS legacy_coordinator_deleted,
       COUNT(*) FILTER (WHERE COALESCE(r.recipient_count,0)=0) AS no_current_recipient_blockers,
       COUNT(*) FILTER (WHERE COALESCE(r.recipient_count,0)>1) AS multiple_current_recipients_review,
       COALESCE(SUM(r.recipient_count),0) AS private_markers_to_backfill,
       COALESCE(MAX(r.recipient_count),0) AS max_recipients_on_one_record
FROM legacy l
LEFT JOIN recipient_counts r ON r.kind=l.kind AND r.record_id=l.record_id
GROUP BY l.kind ORDER BY l.kind;

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
