"""Portable logic smoke test for Janasiri read-only legacy Trash preflight.

Uses SQLite only to test grouping and mapping on synthetic records.
This is NOT a PostgreSQL dialect/EF migration test and NEVER opens a network connection.
Run: python tests/sql/check_legacy_trash_preflight.py
"""
from pathlib import Path
import sqlite3

BASE = Path(__file__).resolve().parents[2]
source = (BASE / "docs" / "PRODUCTION_TRASH_PREFLIGHT_READONLY.sql").read_text(encoding="utf-8-sig")
fixture = (Path(__file__).parent / "legacy_trash_preflight_fixture.sql").read_text(encoding="utf-8-sig")
db = sqlite3.connect(":memory:")
db.executescript(fixture)


def extract(start_marker, end_marker, cte):
    fragment = source.split(start_marker, 1)[1].split(end_marker, 1)[0]
    # Translate only PostgreSQL-specific surface notation for the SQLite logic check.
    statement = cte + fragment.split(cte, 1)[1].strip()
    return (statement
            .replace("'Order'::text", "'Order'")
            .replace("'SalesRep'::text", "'SalesRep'")
            .replace("NOW() - INTERVAL '7 days'", "datetime('now','-7 days')"))


coordinator_sql = extract(
    "-- Legacy role-wide Coordinator deletes do not identify the actor.",
    "\nROLLBACK;",
    "WITH legacy AS")
rows = db.execute(coordinator_sql).fetchall()
columns = [c[0] for c in db.execute(coordinator_sql).description]
by_kind = {r[0]: dict(zip(columns, r)) for r in rows}
expected = {
    "Order": (2, 1, 1, 2, 2),
    "QuickRequest": (1, 0, 1, 2, 2),
    "Quotation": (1, 0, 1, 2, 2),
    "RepPayment": (1, 0, 1, 2, 2),
}
assert set(by_kind) == set(expected), by_kind
for kind, desired in expected.items():
    actual = tuple(by_kind[kind][name] for name in (
        "legacy_coordinator_deleted",
        "no_current_recipient_blockers",
        "multiple_current_recipients_review",
        "private_markers_to_backfill",
        "max_recipients_on_one_record"))
    assert actual == desired, (kind, actual, desired)
    print(f"PASS coordinator {kind}: {actual}")

personal_sql = extract(
    "-- Personal Trash already older than seven days will leave each user's",
    "-- Legacy role-wide Coordinator deletes do not identify the actor.",
    "WITH legacy_personal AS")
records = db.execute(personal_sql).fetchall()
columns = [c[0] for c in db.execute(personal_sql).description]
personal = {(r[0], r[1]): dict(zip(columns, r)) for r in records}
assert personal[("Order", "SalesRep")]["would_expire_on_first_sweep"] == 1
assert personal[("Order", "Coordinator")]["unknown_deletion_date_fresh_window"] == 2
assert personal[("QuickRequest", "SalesRep")]["already_permanent"] == 1
assert personal[("QuickRequest", "Coordinator")]["already_permanent"] == 1
assert personal[("Quotation", "Customer")]["unknown_deletion_date_fresh_window"] == 1
assert personal[("RepPayment", "SalesRep")]["would_expire_on_first_sweep"] == 1
print("PASS personal Trash: old soft deletions, already purged, and unknown dates")

assert "BEGIN TRANSACTION READ ONLY;" in source
assert "ROLLBACK;" in source
assert not any(f in source.upper() for f in ("UPDATE \"ORDERS\" SET", "DELETE FROM \"ORDERS\"", "DROP TABLE", "TRUNCATE TABLE"))
print("PASS SQL review: read-only transaction envelope and no table mutation commands")
print("Synthetic test complete. This does not inspect actual Janasiri production data.")
