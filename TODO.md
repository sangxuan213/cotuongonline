# TV6 Phase 1 - Implementation TODO

Branch: `feature/tv6` (based on `origin/develop` = 8bcab88, verified)

## Bước 1 — Baseline (DONE)
- [x] git fetch origin
- [x] checkout develop + pull --ff-only
- [x] delete feature/tv6, recreate from develop
- [x] push --force-with-lease feature/tv6
- [x] verify merge-base/HEAD/diff/status

## Bước 2 — Implementation
### P1-TV6-D1 — SQLite schema/migration
- [ ] Create XiangqiOnline.Persistence project
- [ ] Schema.sql (schema_versions, players, matches, moves, position_history, FK, unique)
- [ ] DatabaseInitializer (fresh DB migration, idempotent)
- [ ] Add Persistence to solution

### P1-TV6-D2 — Repositories
- [ ] MatchRepository (parameterized SQL, transaction/disposal)
- [ ] MoveRepository (parameterized SQL, unique constraints)
- [ ] Temp SQLite test helper

### P1-TV6-D3 — MoveCommittingService
- [ ] PERSIST_FIRST
- [ ] atomic transaction
- [ ] duplicate clientMoveId protection
- [ ] board_hash_before / board_hash_after
- [ ] rollback on failure
- [ ] DB fail => state/revision unchanged, no MOVE_COMMITTED

### P1-TV6-D4 — Logging
- [ ] Microsoft.Extensions.Logging + Serilog
- [ ] structured logging + correlation
- [ ] token/secret redaction
- [ ] logging never crashes business flow

### P1-TV6-D5 — Integration tests (>=5 REAL)
- [ ] legal move => DB exactly 1 row
- [ ] duplicate retry => still 1 row
- [ ] rejected move => no DB write
- [ ] persistence failure => rollback
- [ ] committed move read-back consistency

### Evidence
- [ ] Extra/test-evidence/phase1/tv6/TV6_Phase1_TestReport.md
- [ ] Extra/test-evidence/phase1/tv6/TV6_Phase1_Handover.md
- [ ] Extra/test-evidence/phase1/tv6/TV6_Phase1_DatabaseEvidence.md
- [ ] Extra/test-evidence/phase1/tv6/TV6_Phase1_LoggingEvidence.md

### Build & Test
- [ ] dotnet restore Code/XiangqiOnline.sln
- [ ] dotnet build Code/XiangqiOnline.sln -c Release
- [ ] dotnet test Code/XiangqiOnline.sln -c Release --no-build
- [ ] Report per-project: Rule/Protocol/Server/Persistence/Integration

### Commit & Push
- [ ] git add + commit
- [ ] git push origin feature/tv6
- [ ] Deliver TV6 Phase 1 Final Report
