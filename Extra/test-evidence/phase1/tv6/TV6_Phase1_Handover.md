# TV6 Phase 1 — Handover

## Summary

TV6 Phase 1 delivers the **persistence layer** for the Xiangqi Online server: SQLite schema/migration, repository pattern, atomic move-commit transaction, structured logging, and real integration tests.

## Branch

- `feature/tv6` — reset to start **directly** from `origin/develop` (`8bcab88`).
- Pushed to `origin/feature/tv6`. **Not merged.**

## Delivered (P1-TV6-D1 → D5)

| Task | Deliverable | Status |
|------|-------------|--------|
| **D1** | SQLite schema + migration (`schema_versions`, `players`, `matches`, `moves`, `position_history`), foreign keys, fresh DB, CI build/test | ✅ |
| **D2** | `MatchRepository`, `MoveRepository`, parameterized SQL, transaction/disposal discipline, unique constraints, temp-DB tests | ✅ |
| **D3** | Atomic transaction, persist-first, duplicate `clientMoveId` protection, rollback, `board_hash_before/after`, DB-fail leaves no partial state / no committed move | ✅ |
| **D4** | `Microsoft.Extensions.Logging` + Serilog, structured logging, correlation, secret redaction, logging never crashes business flow | ✅ |
| **D5** | ≥5 real integration tests (legal→1 row, duplicate→1 row, rejected→0 rows, persistence-fail→rollback, read-back consistency) | ✅ |

## Architecture

New project: **`XiangqiOnline.Persistence`** (`net10.0`)

```
XiangqiOnline.Persistence/
├── Configuration/DatabaseOptions.cs
├── Database/
│   ├── Schema.sql
│   ├── DatabaseInitializer.cs
│   ├── DbConnectionFactory.cs
│   ├── IDbConnectionFactory.cs
├── Logging/
│   ├── CorrelationContext.cs
│   ├── LoggingSetup.cs
│   └── SecretRedactor.cs
├── Models/
│   ├── MatchRecord.cs
│   └── MoveRecord.cs
├── Repositories/
│   ├── IMatchRepository.cs
│   ├── MatchRepository.cs
│   ├── IMoveRepository.cs
│   └── MoveRepository.cs
├── Services/
│   ├── BoardHasher.cs
│   ├── GamePersistenceService.cs
│   ├── MoveCommittingService.cs
│   └── MoveCommitResult.cs
├── IdGenerator.cs
└── XiangqiOnline.Persistence.csproj
```

## Key Behaviors

- **PERSIST_FIRST**: `MoveCommittingService.PersistAtomic` inserts the move row first inside a transaction, then updates board state, then commits.
- **Duplicate protection**: `(match_id, client_move_id)` UNIQUE constraint; `MoveRepository.TryInsert` maps UNIQUE violation (SQLITE_CONSTRAINT_UNIQUE) → `Duplicate`.
- **Rollback**: any non-duplicate failure (e.g. FK violation) propagates → transaction rolls back → `PersistenceFailure`; no partial state, revision/state unchanged.
- **FK discipline**: `MatchRepository` upserts players before inserting a match.
- **Logging**: structured, correlation context, `SecretRedactor` strips tokens/secrets, all logging wrapped so it never throws into business flow.

## Test Results

- RuleEngine.Tests: **229/229 PASS**
- Server.Tests: **19/19 PASS**
- Protocol.Tests: 0 tests (csproj only in baseline)
- IntegrationTests: **20/20 PASS** (≥5 real persistence integration tests)

## Evidence

- `Extra/test-evidence/phase1/tv6/TV6_Phase1_TestReport.md`
- `Extra/test-evidence/phase1/tv6/TV6_Phase1_DatabaseEvidence.md`
- `Extra/test-evidence/phase1/tv6/TV6_Phase1_LoggingEvidence.md`

## Next Steps / For Reviewer

1. Review `XiangqiOnline.Persistence` project and the Server wiring in `Program.cs`.
2. Confirm integration tests in `XiangqiOnline.IntegrationTests/Persistence/` and `Logging/`.
3. Run: `dotnet restore`, `dotnet build -c Release`, `dotnet test -c Release`.
4. Do **not** merge; `feature/tv6` is for review only.
