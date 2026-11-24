# Janasiri — Role-aware Trash and 7-day lifecycle

Feature branch in both repositories: feat/role-aware-trash-7day.

## Architecture decisions

- Admin/SuperAdmin deletion is global. It hides the record from all normal role views.
- Rep, Coordinator and Customer deletion is private to the authenticated user. A personal delete never changes the shared business record's deletion flags.
- UserRecordTrashes stores personal visibility by (UserId, RecordKind, RecordId), with UTC DeletedAt and nullable PurgedAt.
- A personal purge or expiry sets PurgedAt. The record stays hidden from that user's active view and disappears from that user's Trash. It remains visible to other authorized roles.
- Admin purge/expiry uses one guarded AdminRecordTrashPurge transition. It removes the record from Admin Trash but retains the business row, related data and attachments for audit/financial retention.
- Physical hard-delete entry points for Orders and Quick Requests are disabled. No business record or attachment is physically deleted by this feature.
- Admin and personal expiry are based on deletion timestamps, not on opening a Trash page. The background service processes batches of 200 and is idempotent.

## Implemented

### Backend

- Global Admin deletion is enforced in Rep, Coordinator and Customer order/quick-request active lists and direct details.
- Coordinator, Rep and Customer personal Trash now use UserRecordTrashes for Orders, Quick Orders, Quotations and Quick Quotations.
- Standard list/detail/action routes check user ownership/assignment and current Trash visibility. Customer cancellation, rating and reorder are scoped to the authenticated Customer.
- Coordinator order/quotation/quick-request status actions reject records hidden in that Coordinator's personal Trash.
- Individual and bulk personal restore/purge are supported for Orders and Quick Orders; individual restore/purge is supported for Quotations and Quick Quotations.
- Customer Orders and Quotations have personal Trash restore/purge endpoints.
- Admin manual purge is supported for Orders/Quick Orders, Quotations, Quick Quotations and Payment Reports. It only removes items from Admin Trash while retaining business/financial rows and evidence.
- AdminRecordTrashPurge is shared by manual Admin purge and scheduled Admin expiry for Orders, Quick Requests, Quotations and RepPayments.
- TrashCleanupService expires personal business-record Trash and Admin Trash after seven days with an hourly UTC sweep (within one hour of expiry). Existing Rep/Coordinator profile Trash retains its separate 30-day policy. The job does not physically delete business or financial records.
- Legacy Rep, Coordinator and Customer deletion flags are backfilled into UserRecordTrashes by the additive EF migration. Purged-only legacy Coordinator records are included. The SQL blocks rollout if a legacy deletion has no resolvable current Rep/Coordinator/Customer owner.
- Old globally Admin-deleted rows with NULL DeletedAt receive NOW() (new seven-day window); valid legacy timestamps remain unchanged. Duplicate Delete requests never reset an existing or purged user's Trash marker.
- Old role-delete fields remain for reference, but rollback AFTER new personal deletions needs explicit reconciliation: dropping UserRecordTrashes would re-expose those new private deletions.
- Quotation and RepPayment have IsPurgedByAdmin and AdminPurgedAt fields so expired Admin Trash does not reappear. Legacy RepPayment deletion states are backfilled into per-user Trash.
- Trash DTOs return the actual personal DeletedAt, so 7-day countdowns and ordering use the new table timestamps.

### Frontend

- Admin Quotations Trash includes standard and Quick Quotations, Restore and “Remove from Admin Trash”.
- Rep and Coordinator Quotation Trash supports Restore and personal “Remove”.
- Customer Quotations has a personal Trash tab, Move to Trash, Restore and Remove.
- Customer Orders Trash supports Restore and Remove.
- Unified Orders/Quick Orders Trash has role-aware Admin vs personal wording and confirmation.
- Retention copy now explains that records remain stored for audit.
- Rep and Coordinator Payment Reports include a personal Remove from Trash action, and Admin Payment Reports uses non-destructive Remove from Admin Trash wording. Evidence remains available to its authorized owner within personal Trash, but not after personal purge.

## Validation completed

- Backend API project builds successfully.
- Backend unit tests: **187 passed, 0 failed**, including Admin operational dashboard vs historical financial-report separation and 3 migration rollback-guard regression tests. Isolated PostgreSQL 17 integration run: **6 passed, 0 failed** (4 real PostgreSQL Trash concurrency/expiry checks, HTTP 409 response check, 1 pre-existing placeholder test).
- Frontend production build (tsc -b and vite build) succeeds.
- dotnet ef migrations has-pending-model-changes reports no model changes pending.
- git diff --check is run before commits.
- Coordinator-to-Coordinator, Rep-to-Customer, Customer-to-Rep isolation, direct-detail bypass, action bypass, duplicate Delete, manual purge, 7-day expiry, idempotent rerun and non-destructive retention have regression coverage.
- A disposable local PostgreSQL 17 test recreated the old EF schema, seeded old Admin/Rep/Coordinator/Customer deletion combinations, confirmed fail-closed migration for an orphan Coordinator record, then passed all three additive migrations when the orphan was resolved. Postcheck: 12 expected personal markers, 0 missing, 0 lost permanent hides, 0 Admin deletion timestamps left NULL.
- Two read-only deployment queries are included: PRODUCTION_TRASH_PREFLIGHT_READONLY.sql and PRODUCTION_TRASH_POSTMIGRATION_READONLY.sql. Preflight now explicitly reports multi-Coordinator historic deletion mappings and immediately expiring old **personal** Trash (not just Admin Trash); a synthetic SQLite logic fixture is in tests/sql/.
- API security findings, Rep ledger/payment authorization fixes, optimistic concurrency protection for personal AND global Admin Trash, and remaining production compatibility risks are documented in TRASH_SECURITY_AND_MIGRATION_AUDIT.md. POSTGRES_TRASH_CONCURRENCY_TEST_RUNBOOK.md gives the opt-in local PostgreSQL test procedure.

## Remaining before merge/deployment

1. Run PRODUCTION_TRASH_PREFLIGHT_READONLY.sql with a read-only account against a production snapshot. Record historical counts, unmappable rows and records older than seven days. A disposable scratch fixture was tested, but real production data has NOT been inspected.
2. Main QuickRequest image/PDF, Payment evidence, Quotation details and status/upload bypasses were audited and fixed. Rep ledger/payment endpoints now check assignment and cross-customer linkage. Admin dashboard active/pending Order KPIs now exclude globally deleted Orders without changing historical financial totals. Complete client-generated CSV/Excel/PDF reports, customer documents, remaining KPI semantics and full region-report UAT; see TRASH_SECURITY_AND_MIGRATION_AUDIT.md.
3. Run the migration on a production-like restored staging database, verify postmigration SQL counts, restore/purge behavior and linked financial/evidence records. Review old records that will expire during the first cleanup sweep; get explicit approval before enabling the job.
4. Real PostgreSQL stale-write and expiry tests passed. Verify the background service under a multi-process staging topology with logs/metrics, retry semantics and load tests. Stale writes are rejected, but no explicit distributed leader lock has been added.
5. Complete desktop/mobile role-by-role UAT for Admin, Rep, Coordinator and Customer. The detailed execution matrix and release criteria are in ROLE_TRASH_STAGING_UAT_CHECKLIST.md (not yet executed on staging).
6. Run repository CI, open separate backend/frontend PRs, review the diffs and request approval. Do not auto-merge or deploy production.

## Deployment safeguards

- Production API startup and Docker Compose default to NO automatic EF migrations. Set DB_AUTOMIGRATE explicitly only under a controlled migration plan. The hourly TrashCleanup job is also disabled by default in production; enable TRASH_CLEANUP_ENABLED only after preflight, staging validation and approval.
- No production database migration has been run.
- No production deployment has been performed.
- main has not been modified.
- Do not physically delete business rows or stored attachments under this feature. Any future physical retention/deletion policy must be a separate approved change with FK, financial, legal and audit review.
- Keep local appsettings.Development.json changes and unrelated frontend backup folders out of commits.
- Feature deployment needs a compatible database/schema first, and legacy writers must be paused while backfill and API rollout are coordinated. Back up the DB AND evidence storage. Downgrade migration does not reconstruct new user-deletion markers, and now fails closed for nonempty personal Trash or purged Quotations/RepPayments until reconciled.
- Frontend PR #2 (fix/frontend-ci-critical-audit) remains separate; reconcile/rebase after it is merged before opening the final feature PR.
