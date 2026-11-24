# Janasiri — Role-aware Trash security & migration audit

Scope: local backend/frontend feature branch feat/role-aware-trash-7day. No production writes.

## Source-level fixes verified

- Rep QuickRequest upload now checks ownership, global Admin deletion and the Rep's personal Trash marker before saving an attachment.
- Admin QuickRequest status changes reject globally deleted/purged records.
- Admin active Quotation detail/list exclude globally deleted/purged records.
- QuickRequest attachment GET checks owning Rep/assigned Coordinator; private Trash is readable only until purge. Admin can inspect Admin Trash attachments until purge; normal active detail GET is still restricted.
- Admin active RepPayment detail excludes globally deleted/purged records. Evidence GET has narrow Admin Trash access; Rep/Coordinator evidence is scoped to personal Trash until purge.
- Duplicate personal Delete cannot reset the DeletedAt timestamp or resurrect a permanently hidden marker.
- Legacy Coordinator purge-only flags are included in the migration, including cases where deleted=false but purged=true.
- Migration fails closed on legacy deletions whose owner/profile/Coordinator cannot be resolved.
- Missing legacy Admin deletion timestamps are set to NOW() only when missing, without overwriting valid dates.
- Production startup migrations and hourly Trash cleanup are disabled by default in Compose. They require separate, controlled approval.
- Rep customer-balance/ledger reads now validate the authenticated Rep's current Customer assignment; a guessed CustomerId from another Rep no longer works.
- Rep payment creation now checks current Customer assignment and ensures any linked OrderId belongs to that Customer; nonpositive payment amounts are rejected.
- Accounting ledger intentionally retains historical globally deleted Orders and their payments. This prevents deletion from silently changing existing balances.
- Admin dashboard operational TotalOrders/PendingOrders now excludes globally deleted/purged Orders; historical TotalSalesToday/Month, sales trends and financial reports preserve original accounting values. A dedicated 3-test regression suite verifies separation, personal Rep deletes, and retained historical sales.
- UserRecordTrash.PurgedAt is an optimistic concurrency token. A stale Restore cannot remove a marker already permanently purged by another request; a stale Purge cannot recreate a concurrently restored marker. Concurrent duplicate insert unique-key violations are handled as a missing active item.
- Global Admin deletion and purge flags are optimistic concurrency tokens for Order, QuickRequest, Quotation, RepPayment. Stale 7-day cleanup cannot purge an Admin-restored record; concurrent Admin purge/restore rejects stale writes with HTTP 409 instead of overwriting fresh state.
- Additive ProtectUserRecordTrashConcurrency and ProtectAdminTrashConcurrency migrations update EF model metadata only (empty Up/Down): they do not modify table schema/data. They still require controlled migration sequencing as part of the overall feature rollout.
- The three destructive downgrade paths are now fail-closed: dropping UserRecordTrashes is rejected when private markers exist; removing Quotation or RepPayment Admin purge flags is rejected when purged records exist. These checks were executed against disposable PostgreSQL 17, including safe-empty and blocked-populated cases (3/3 passed).

## Checks

- Latest full backend unit suite: **187 passed, 0 failed**, including 3 dashboard/financial separation tests and 3 rollback-guard regression tests. Latest integration suite with isolated PostgreSQL 17 environment: **6 passed, 0 failed**, including 4 real PostgreSQL concurrency/expiry checks and HTTP 409 conflict middleware; plus the pre-existing placeholder test. Frontend production build previously passed.
- Previous frontend production build passed. Frontend files were not altered during this API-audit pass.
- Disposable local PostgreSQL 17 test: intentionally orphaned Coordinator record blocked migration. After fixing dummy data, three new migrations succeeded.
- Local scratch postmigration review: 12 expected legacy personal markers; 0 missing, 0 lost permanent-hidden markers, 0 missing Admin deletion dates.
- No actual production snapshot, Janasiri staging database, or configured local Janasiri production connection was found under the checked local project/configuration. The existing OneBiz PostgreSQL database was left untouched. Therefore actual production historic deletion counts are still UNKNOWN.
- The read-only preflight now reports **current per-record Coordinator recipient cardinality** (zero and multiple assignments) across Orders, QuickRequests, Quotations and RepPayments. Old shared Coordinator deletion flags cannot identify the actual historical actor; multiple recipients need manual review before migration.
- A separate preflight summary counts **legacy personal Trash that would expire on the first hourly sweep**, permanently hidden legacy entries and missing deletion dates, grouped by record kind and role. It is a planning report, not a decision to run cleanup.
- Portable synthetic SQLite logic tests (`python tests/sql/check_legacy_trash_preflight.py`) passed for 4 Coordinator mapping scenarios and personal retention cutoffs. This is not a PostgreSQL dialect check; the Docker engine was offline during this new test run.
- This does NOT validate the actual production database.

## Deployment blockers / remaining reviews

1. Run PRODUCTION_TRASH_PREFLIGHT_READONLY.sql against a restored production snapshot using a read-only account. Review null dates, deleted counts, records >7 days, orphaned assignments, purge-only flags.
2. Historical coordinator ownership is ambiguous: old shared Coordinator-delete flags do not capture the actor. Backfill preserves old role-wide deletion for CURRENT assigned Coordinators. Reassignment since deletion may need specific business-approved reconciliation.
3. Records globally deleted >7 days ago will leave Admin Trash on first enabled cleanup sweep. They remain in storage. Approve timing and counts first.
4. PostgreSQL 17 scratch tests now cover personal stale Restore-versus-Purge, Admin Restore-versus-expiry, duplicate cleanup processing and idempotent hourly expiry; 4/4 passed. Remaining production gate: truly simultaneous multi-process load tests (including old API writers), plus staging UAT against a restored production-like database and approval of scheduler ownership/leader strategy.
5. Complete authenticated API tests and desktop/mobile UI testing for Admin, Rep, Coordinator, Customer, including image/evidence access, orders, quick orders, quotations, quick quotations, payment reports.
6. Financial API review: Rep ledger/customer payment IDOR fixed and tested. Region-scoped Stock/Sales Summary/Outstanding Reports and Rep-owned Target Report endpoints were spot-checked; role/region checks are present. Admin operational dashboard Order counts now exclude globally deleted records; financial sales totals and historical reporting intentionally remain intact. Review client-generated CSV/XLSX/PDF exports, customer documents, KPIs beyond Order counts, and remaining aggregate routes in staging.
7. Actual rollback SQL now refuses to drop nonempty personal-trash markers or remove active Admin purge flags for Quotations/RepPayments. A controlled rollback requires business-approved data reconciliation and verified backups; the safe failure is intentional.
8. Coordinate old-API writer freeze, additive migration, read-only postmigration checks, new API/frontend rollout. Do not enable cleanup until approved.
9. Finish CI review and separate feature PRs. Leave frontend audit PR #2 untouched.

## Read-only SQL checks

- docs/PRODUCTION_TRASH_PREFLIGHT_READONLY.sql
- docs/PRODUCTION_TRASH_POSTMIGRATION_READONLY.sql

No production database, backend/frontend server, production files or main branch was modified.