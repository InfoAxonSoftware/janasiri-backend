-- Janasiri Trash migration POSTCHECK. READ ONLY. Run on staging after migration.
-- Query shows missing, wrongly unpurged entries, and preserved Admin status.
BEGIN TRANSACTION READ ONLY;

SELECT "MigrationId" FROM "__EFMigrationsHistory"
WHERE "MigrationId" LIKE '%AddUserRecordTrash%'
   OR "MigrationId" LIKE '%AddQuotationAdminPurgeAudit%'
   OR "MigrationId" LIKE '%AddRepPaymentAdminPurgeAudit%'
ORDER BY "MigrationId";

SELECT "RecordKind",
       COUNT(*) AS entries,
       COUNT(*) FILTER (WHERE "PurgedAt" IS NOT NULL) AS permanently_hidden,
       COUNT(*) FILTER (WHERE "PurgedAt" IS NULL) AS in_personal_trash
FROM "UserRecordTrashes"
GROUP BY "RecordKind"
ORDER BY "RecordKind";

WITH expected AS (
    SELECT 'Order' AS kind, o."Id" AS record_id, rp."UserId" AS user_id, o."IsPurgedByRep" AS should_be_purged
    FROM "Orders" o JOIN "SalesRepProfiles" rp ON rp."Id" = o."RepId"
    WHERE o."IsDeletedByRep" OR o."IsPurgedByRep"
    UNION
    SELECT 'Order', o."Id", cp."UserId", o."IsPurgedByCoordinator"
    FROM "Orders" o JOIN "CustomerProfiles" c ON c."Id" = o."CustomerId"
    JOIN "CoordinatorProfiles" cp ON cp."Id" = c."AssignedCoordinatorId"
    WHERE o."IsDeletedByCoordinator" OR o."IsPurgedByCoordinator"
    UNION
    SELECT 'Order', o."Id", cp."UserId", o."IsPurgedByCoordinator"
    FROM "Orders" o JOIN "RepCoordinators" rc ON rc."RepId" = o."RepId"
    JOIN "CoordinatorProfiles" cp ON cp."Id" = rc."CoordinatorId"
    WHERE o."IsDeletedByCoordinator" OR o."IsPurgedByCoordinator"
    UNION
    SELECT 'Order', o."Id", cp."UserId", false
    FROM "Orders" o JOIN "CustomerProfiles" cp ON cp."Id" = o."CustomerId"
    WHERE o."IsDeletedByCustomer"
    UNION
    SELECT 'QuickRequest', q."Id", rp."UserId", q."IsPurgedByRep"
    FROM "QuickRequests" q JOIN "SalesRepProfiles" rp ON rp."Id" = q."RepId"
    WHERE q."IsDeletedByRep" OR q."IsPurgedByRep"
    UNION
    SELECT 'QuickRequest', q."Id", cp."UserId", q."IsPurgedByCoordinator"
    FROM "QuickRequests" q JOIN "RepCoordinators" rc ON rc."RepId" = q."RepId"
    JOIN "CoordinatorProfiles" cp ON cp."Id" = rc."CoordinatorId"
    WHERE q."IsDeletedByCoordinator" OR q."IsPurgedByCoordinator"
    UNION
    SELECT 'Quotation', q."Id", rp."UserId", false
    FROM "Quotations" q JOIN "SalesRepProfiles" rp ON rp."Id" = q."RepId"
    WHERE q."IsDeletedByRep"
    UNION
    SELECT 'Quotation', q."Id", cp."UserId", false
    FROM "Quotations" q JOIN "CoordinatorProfiles" cp ON cp."Id" = q."CoordinatorId"
    WHERE q."IsDeletedByCoordinator"
    UNION
    SELECT 'Quotation', q."Id", cp."UserId", false
    FROM "Quotations" q JOIN "RepCoordinators" rc ON rc."RepId" = q."RepId"
    JOIN "CoordinatorProfiles" cp ON cp."Id" = rc."CoordinatorId"
    WHERE q."IsDeletedByCoordinator"
    UNION
    SELECT 'Quotation', q."Id", cp."UserId", false
    FROM "Quotations" q JOIN "CustomerProfiles" cp ON cp."Id" = q."CustomerId"
    WHERE q."IsDeletedByCustomer"
    UNION
    SELECT 'RepPayment', p."Id", rp."UserId", false
    FROM "RepPayments" p JOIN "SalesRepProfiles" rp ON rp."Id" = p."RepId"
    WHERE p."IsDeletedBySalesRep"
    UNION
    SELECT 'RepPayment', p."Id", cp."UserId", false
    FROM "RepPayments" p JOIN "RepCoordinators" rc ON rc."RepId" = p."RepId"
    JOIN "CoordinatorProfiles" cp ON cp."Id" = rc."CoordinatorId"
    WHERE p."IsDeletedByCoordinator"
)
SELECT COUNT(*) AS expected_legacy_user_markers,
       COUNT(*) FILTER (WHERE t."Id" IS NULL) AS missing_personal_markers,
       COUNT(*) FILTER (WHERE e.should_be_purged AND t."PurgedAt" IS NULL) AS legacy_purges_lost
FROM expected e
LEFT JOIN "UserRecordTrashes" t ON t."RecordKind" = e.kind
    AND t."RecordId" = e.record_id AND t."UserId" = e.user_id;

SELECT 'Orders' AS kind,
       COUNT(*) FILTER (WHERE "IsDeleted") AS globally_deleted,
       COUNT(*) FILTER (WHERE "IsDeleted" AND "DeletedAt" IS NULL) AS missing_admin_delete_date
FROM "Orders"
UNION ALL
SELECT 'QuickRequests',
       COUNT(*) FILTER (WHERE "IsDeletedByAdmin"),
       COUNT(*) FILTER (WHERE "IsDeletedByAdmin" AND "AdminDeletedAt" IS NULL)
FROM "QuickRequests"
UNION ALL
SELECT 'Quotations',
       COUNT(*) FILTER (WHERE "IsDeleted"),
       COUNT(*) FILTER (WHERE "IsDeleted" AND "DeletedAt" IS NULL)
FROM "Quotations"
UNION ALL
SELECT 'RepPayments',
       COUNT(*) FILTER (WHERE "IsDeletedByAdmin"),
       COUNT(*) FILTER (WHERE "IsDeletedByAdmin" AND "AdminDeletedAt" IS NULL)
FROM "RepPayments";

ROLLBACK;

-- PASS only if missing_personal_markers = 0, legacy_purges_lost = 0
-- and missing_admin_delete_date = 0. Review affected counts before enabling
-- the hourly cleanup. Use a sanitized staging DB as deployment gate.
