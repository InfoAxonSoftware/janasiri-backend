# Janasiri — Role-aware Delete / Trash UAT acceptance checklist

**Environment:** isolated staging, backed by a sanitized copy of the expected production schema and synthetic users/records. **Never run destructive tests against production**. Production API/DB/frontend are read-only for this project until explicit user authorization. The production cleanup and automatic migrations must remain disabled.

Feature branch: `feat/role-aware-trash-7day` (backend and frontend).

## Test data prerequisites

Create synthetic:
- Two Admins (A1, A2); two Sales Reps (R1, R2); two Sales Coordinators (C1, C2) who can both be assigned to R1; Customer U1 assigned to R1/C1 and U2 assigned to R2/C2.
- Active normal Orders O1/O2, Quick Orders QO1/QO2, normal Quotations QT1/QT2, Quick Quotations QQ1/QQ2; linked financial Order/Payment/RepPayment evidence.
- One legacy Admin-deleted Order, one legacy Rep-deleted Quotation, one Coordinator-purged-only Quick Request, one Customer-deleted Order, and one old Admin-deleted RepPayment (with private attachment), plus intentionally old dates.
- A user role re-assignment scenario with deleted records (R1->R2 and C1->C2) **for inspection**, not automatic reconciliation.

## Required acceptance scenarios

| Case | Action / scenario | Expected result |
|---|---|---|
| A01 | Admin A1 deletes active O1 | O1 absent from Admin, R1, C1, U1 active lists and direct-detail API. O1 visible in Admin Trash |
| A02 | Admin A1 deletes QO1 | QO1 hidden from Admin, owning Rep and assigned Coordinator; still in Admin Trash; no Customer Quick Request route |
| A03 | Admin A1 deletes QT1 / QQ1 | Both globally hidden; Admin Quotation Trash handles normal + quick varieties |
| A04 | Admin A1 deletes RepPayment P1 | P1 globally hidden but its payment and evidence storage rows remain |
| A05 | Admin A2 restores O1/QO1/QT1/QQ1/P1 | Normal active visibility returns, subject to each user's earlier private Trash markers |
| A06 | Admin purges an item from Admin Trash | Item disappears from Admin Trash, never reappears active; business/financial row and evidence retained; later restore denied |
| A07 | Admin tries to update status of globally deleted Order/QuickRequest/Quotation/Payment Report through direct API | Request is denied; no side effects |
| A08 | Admin accesses private Quick Request/Payment evidence from Admin Trash | Allowed only until Admin Trash purge; private downloads never anonymous |
| R01 | Rep R1 privately deletes O1/QO1/QT1/QQ1/RepPayment P1 | Hidden from R1 only; Admin, C1 and U1 (where applicable) remain unaffected |
| R02 | R1 restores private Trash item | Returns to R1 active list if Admin has not globally deleted it |
| R03 | R1 permanently removes private Trash item | Removed from R1 Trash; remains hidden from R1 active list; other authorized users unaffected |
| R04 | R1 tries to access R2's Order, ledger or Payment Report by changing an ID | Denied; no cross-Rep data leakage |
| R05 | R1 tries direct attachment upload to private-deleted or Admin-deleted Quick Request | Denied; no files written |
| R06 | R1 downloads attachment from own Trash item, then purges it | Download before purge allowed; after purge denied |
| C01 | C1 privately deletes O1/QO1/QT1/QQ1/P1; C2 also has visibility | Only C1 view changes. C2 remains authorized as before |
| C02 | C1 restores/purges items | Changes only C1 Trash markers, not C2's view |
| C03 | C1 attempts status update or direct detail access on own private-deleted item | Active detail/status endpoint denied; scoped Trash attachment access until purge |
| C04 | Assigned Coordinator changed after historic deletion | Inspect ambiguity; do not automatically claim deleted actor is known |
| U01 | U1 privately deletes normal Order O1 and Quotation QT1 | Hidden only for U1; Admin/Rep/Coordinator unaffected |
| U02 | U1 restores or permanently removes O1/QT1 from own Trash | Restore permitted before purge; after purge stays hidden for U1; other roles unchanged |
| U03 | U1 attempts U2's Order/detail/action by GUID | Denied by ownership validation |
| U04 | U1 attempts cancel/rate/reorder of own globally-deleted or privately-deleted Order | Denied through active workflows |
| B01 | Repeated Delete request, on active Trash entry and purged marker | Must not reset DeletedAt or PurgedAt; no reappearance |
| B02 | Two concurrent Restore/Purge requests | Loser receives conflict/not-found, stale update cannot expose a permanently hidden item |
| B03 | Two cleanup workers process same eligible Admin record | At most one successful purge; no physical deletion; eventual subsequent sweep safe |
| B04 | 7-day retention threshold: 6d23h, exactly 7d, 7d1h | Expiration follows '< cutoff' UTC rule; first hourly sweep after threshold finalizes; no automatic restore |
| B05 | Expiry twice | Second pass idempotent, changes 0 records |
| B06 | Old Admin-deleted item without DeletedAt | Migration gives NEW 7-day window, does not overwrite non-NULL legacy date |
| B07 | Old Coordinator purge-only flag, deleted flag false | Backfilled as permanently hidden per currently assigned Coordinator |
| B08 | Orphan legacy user/role mapping | Migration fails closed; does not silently re-expose records |
| B09 | Legacy Admin deleted item >7 days | Reviewer approves the count **before** enabling first scheduled cleanup |
| B10 | Backward rollback after new private deletions | Test reconciliation plan; dropping UserRecordTrashes alone is prohibited |
| F01 | Admin global delete normal Order | Operational TotalOrders/PendingOrders exclude it; historical sales, customer ledger and payment values are preserved |
| F02 | Rep private delete normal Order | Admin operational and financial totals unchanged |
| F03 | Sales/Outstanding reports and generated PDF/Excel/CSV | Historical accounting source/period and scope remain explicit; no unauthorized customer/region rows |
| F04 | Rep Payment Create with unassigned CustomerId or different Customer's OrderId | Denied; no payment row created |
| UI01 | Desktop and mobile: all four roles | Trash visibility, count, empty state, restore and permanent remove buttons are correct |
| UI02 | Desktop/mobile confirming permanent remove | Copy says 'remove from view/Trash' and discloses retained business/evidence rows, never falsely says physical deletion |
| UI03 | Session cache after delete/restore | Lists, tabs, badges and details refresh, no stale item clickable to bypass API scope |

## Release gate

- [ ] Use **read-only** preflight query against sanitized production snapshot: `PRODUCTION_TRASH_PREFLIGHT_READONLY.sql`
- [ ] 0 orphan ownership blockers; review original historical Admin-delete counts and missing timestamps
- [ ] Snapshot-backed staging migration completed successfully
- [ ] Postmigration query: 0 missing markers / 0 lost permanent markers / 0 missing Admin DeletedAt
- [ ] Explicit reconciliation for historic Coordinator assignments and users reassigned after deletion
- [ ] Automated unit + isolated PostgreSQL integration tests and CI green
- [ ] Browser/API UAT matrix above signed off by reviewer
- [ ] Storage/database backups and verified rollback/recovery approach, including new private Trash data; assert Down() raises an exception for nonempty UserRecordTrashes and purged Quotation/RepPayment flags
- [ ] Release sequence approved: pause legacy writes, additive migration, postcheck, API/frontend deployment
- [ ] Only after explicit approval: enable hourly cleanup in intended scheduler topology
- [ ] Do not merge, deploy or change production systems as part of this checklist

## Last local verification

- Backend unit suite: **187 / 187 passed** (includes 3 dashboard operational-vs-financial and 3 rollback guard regression tests).
- PostgreSQL-backed integration test suite previously: **6 / 6 passed** using temporary, isolated local PostgreSQL.
- Backend API build: 0 errors, 0 warnings (latest local check).
- Frontend production build passed in prior phase; no frontend modifications during this reporting pass.
- No production writes or Git main changes.
