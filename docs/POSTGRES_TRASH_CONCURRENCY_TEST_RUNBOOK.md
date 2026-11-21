# Janasiri — isolated PostgreSQL Trash concurrency test runbook

These tests are opt-in; they only connect to an explicit **local scratch** PostgreSQL at `127.0.0.1:55469/janasiri_trash_scratch`. They refuse any other host/port/database. Never pass production credentials. When the environment variable is absent, the PostgreSQL tests do not execute their database checks (do not count those as PostgreSQL verification).

## Temporary scratch container (local development only)

```powershell
docker run --rm -d --name janasiri-trash-only-pg-test `
  -e POSTGRES_PASSWORD='CHOOSE_A_LOCAL_TEST_PASSWORD' `
  -e POSTGRES_DB=janasiri_trash_scratch `
  -p 127.0.0.1:55469:5432 postgres:17-alpine

$env:JANASIRI_TRASH_SCRATCH_CONNECTION = 'Host=127.0.0.1;Port=55469;Database=janasiri_trash_scratch;Username=postgres;Password=CHOOSE_A_LOCAL_TEST_PASSWORD'
dotnet test .\tests\DistributionSystem.IntegrationTests\DistributionSystem.IntegrationTests.csproj --filter 'FullyQualifiedName~PostgresTrashConcurrencyTests'
Remove-Item Env:\JANASIRI_TRASH_SCRATCH_CONNECTION
docker stop janasiri-trash-only-pg-test
```

**Important:** No production database or existing application container should be touched. The scratch container has no persistent volume and is deleted on stop.

## Validated on 2026-11-21

- 4/4 PostgreSQL 17 tests passed with an explicit local scratch connection.
- Tested personal stale Restore after purge; Admin stale expiry after Restore; two cleanup readers contending on one Admin-deleted record; repeated cleanup idempotence and business-row retention.
- Full unit suite: 181/181 passed.
- Full integration suite with explicit scratch environment: 6/6 passed (four PostgreSQL tests, one conflict HTTP 409 test, one existing placeholder).
- EF model pending-change check: no pending changes.
- Both EF concurrency migrations have empty Up/Down SQL; EF model metadata is updated.

## Limits

This suite simulates stale writes with independent database contexts and confirms real PostgreSQL optimistic concurrency. It is not a stress/load test with multiple running API processes. A restored production-like staging DB and coordinated old-version writer freeze are still deployment blockers. Scheduler conflicts can interrupt one run and will need subsequent retry; do not enable multiple scheduler instances without an agreed deployment strategy.
