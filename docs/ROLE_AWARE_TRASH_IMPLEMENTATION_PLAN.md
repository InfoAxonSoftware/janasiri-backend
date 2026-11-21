# Janasiri — Role-aware trash and 7-day lifecycle plan

Feature branch (both repositories): `feat/role-aware-trash-7day`. Base: `main`. No changes to local development configuration or unrelated backups.

## Required behavior
1. Admin/SuperAdmin soft-delete is **global** across active lists, direct detail APIs, downloads, exports, and action endpoints. Admin restore removes only global hide; previous personal hides are preserved.
2. Rep, Coordinator, Customer deletion is **per user** (not per role): no changes to other users' active records. Access to a record and ownership/assignment must be validated server-side.
3. Admin Trash and personal Trash support individual and bulk restore; purge is two-stage (visibility and data retention).
4. A 7-day deadline uses a UTC timestamp set at deletion, not a page-open event. A backend job processes expired trash, including Orders, Quotations, Quick Orders, Quick Quotations.
5. Admin manual permanent delete and automatic expiry use the **same guarded purge service**. Quick request image/attachment files are deleted only after a successful safe purge, with retry/cleanup logs on storage failure.
6. Financial audit guard: paid, dispatched, delivered, settled, invoiced or linked records must not be silently physically removed. Preserve them in protected retention/archive (hidden globally); only unlinked safe drafts can be physically deleted. Enforce FK constraints and audit logging.
7. Personal permanent removal/expiry only permanently hides from that user and removes the item from their Trash. It never removes database business records.
8. Maintenance must be idempotent, retry-safe, paginated/batched and safe for multiple application instances (leader/advisory lock or DB lock).

## Implementation order and gates

### Phase 1: Global visibility consistency
- Make current admin delete flags authoritative in every role's active list, unified list, detail endpoint and attachment authorization.
- Update misleading 30-day frontend copy to 7 days.
- Add regression tests (admin delete, restore, quick requests; no bypass via ID).
- Gate: backend builds and tests; frontend build.

### Phase 2: Per-user visibility migration
- Introduce an indexed `UserRecordTrash` table keyed by (`UserId`, `Kind`, `RecordId`) with `DeletedAt` and `PurgedAt`.
- Migrate legacy Rep/Coordinator/Customer flags to per-user entries, including coordinator membership; never lose existing delete states.
- Switch the four record types' lists/details/trash/restore/individual/bulk actions to one user-scoped service; enforce ownership.
- Gate: user A deletion cannot hide user B's record; backward-compatible migration tests.

### Phase 3: Permanent purge and scheduling
- Centralize global purge for four record kinds. Implement protected financial retention/archiving and relational/attachment safeguards.
- Replace on-read quotation purge and personal flag-reset behavior with background job. Process 7-day expiration across all record kinds, log failures and retry.
- Gate: manual + automatic purge tests with paid/unpaid orders, converted quotations, attachments, linked records and reruns.

### Phase 4: Frontend UX
- Admin: global delete label + confirmation; Trash restore, manual permanent delete and 7-day countdown for all four kinds.
- Rep/Coordinator/Customer: "Remove from My View" and own Trash restore/permanent-hide; no controls that imply they can erase the database.
- Unified order/quick-order trash, quotation/quick-quotation trash; fix selection, bulk operations, empty-trash, errors, cache invalidation.
- Gate: desktop/mobile role-by-role UAT.

### Phase 5: Deployment verification
- Review generated EF Core migration before applying; migrate staging, test with sanitized records. Backup and dry-run purge review.
- Run CI and staging; verify background runner operation with telemetry. Require approval before production migration and merge.
- Update tests/docs and open separate backend/frontend pull requests. Never auto-merge or deploy production.

## Existing issues confirmed from source
- Existing global Admin delete for Orders/QuickRequests was *not* enforced in Rep/Coordinator/Customer lists.
- Existing ordinary Quotations list already respected global Admin soft-delete.
- Existing legacy `IsDeletedByCoordinator` is a shared boolean and cannot isolate two coordinators.
- Existing quotation Trash screens said 30 days, while code used 7.
- Existing quotation admin Trash read physically purged old quotations; other role Trash reads **reset** expired flags, resurfacing the items.
- Existing Order/QuickRequest role "permanent" action only sets purge flags; it does not physically remove database rows.

## Rollout / safeguards
- No production database access is assumed. Do **not** run schema updates without staging approval.
- Local `appsettings.Development.json` and existing unrelated frontend untracked directories must remain untouched and uncommitted.
- Existing `frontend` PR #2 (`fix/frontend-ci-critical-audit`) remains independent until merged; rebase feature work on current `main` at review.
