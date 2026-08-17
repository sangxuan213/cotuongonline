# TV6 Phase 1 — Test Report

## Overview

- **Branch**: `feature/tv6`
- **Baseline**: directly from `origin/develop` (`8bcab88`)
- **Phase**: TV6 Phase 1 (Persistence — SQLite / Repository / Transaction / Logging / Integration Tests)
- **Date**: (generated on completion)

## Build Status

`dotnet build -c Release` → **Build succeeded, 0 Errors**

## Test Results (per project)

| Project | Total | Passed | Failed | Skipped |
|---------|-------|--------|--------|---------|
| XiangqiOnline.RuleEngine.Tests | 229 | 229 | 0 | 0 |
| XiangqiOnline.Server.Tests | 19 | 19 | 0 | 0 |
| XiangqiOnline.Protocol.Tests | 0 | 0 | 0 | 0 |
| XiangqiOnline.IntegrationTests | 20 | 20 | 0 | 0 |
| **TOTAL** | **268** | **268** | **0** | **0** |

> `XiangqiOnline.Protocol.Tests` has no test source files in this baseline (csproj only), so it reports 0 tests.

## Integration Test Coverage (P1-TV6-D5)

The `XiangqiOnline.IntegrationTests` project contains **20 real tests** against a real SQLite database (temporary file), including the ≥5 required persistence tests in `Tv6PersistenceIntegrationTests`:

| Test | Requirement | Result |
|------|-------------|--------|
| `Legal_move_commits_exactly_one_db_row` | Legal move → exactly 1 DB row | ✅ PASS |
| `Duplicate_clientMoveId_retry_still_one_row` | Duplicate retry → still 1 row | ✅ PASS |
| `Rejected_move_creates_zero_new_rows` | Rejected move → 0 new rows | ✅ PASS |
| `Persistence_failure_rolls_back_no_partial_state` | Persistence failure → rollback, no partial state | ✅ PASS |
| `Persistence_failure_does_not_change_revision_or_state` | DB fail → revision/state unchanged | ✅ PASS |
| `Committed_move_read_back_is_consistent` | Committed move read-back consistency | ✅ PASS |
| `Board_hash_before_and_after_are_recorded` | board_hash_before / board_hash_after recorded | ✅ PASS |

Additional coverage:
- `Tv6DatabaseSchemaTests` — schema/migration/foreign key/unique constraint validation
- `Tv6RepositoryTests` — parameterized SQL, transaction & disposal, unique constraints
- `Tv6LoggingTests` — structured logging, correlation, redaction, no-crash

## Command

```
dotnet restore
dotnet build -c Release
dotnet test -c Release
```

## Conclusion

✅ No test failures. ✅ No mandatory test skipped. ✅ Integration ≥ 5 real tests. ✅ Branch based on `develop`.
