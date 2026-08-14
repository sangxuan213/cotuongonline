# TV6 Phase 1 — Database Evidence

## Schema (P1-TV6-D1)

SQLite schema defined in `Code/src/XiangqiOnline.Persistence/Database/Schema.sql`.

### Tables

| Table | Purpose |
|-------|---------|
| `schema_versions` | Migration tracking (schema version, applied_at_utc) |
| `players` | Player records (player_id PK, display_name, UNIQUE display_name) |
| `matches` | Match records (match_id PK, white/black player FK → players, revision, status, created_at_utc) |
| `moves` | Move records (move_id PK, FK → matches, UNIQUE(match_id, client_move_id), board_hash_before/after, move_number, result) |
| `position_history` | Board position history (FK → moves) |

### Foreign Keys

- `matches.white_player_id` → `players(player_id)`
- `matches.black_player_id` → `players(player_id)`
- `moves.match_id` → `matches(match_id)`
- `position_history.move_id` → `moves(move_id)`

### Unique Constraints

- `players.display_name` UNIQUE
- `moves(match_id, client_move_id)` UNIQUE — protects duplicate client move retries

## Migration

`DatabaseInitializer` applies `Schema.sql` idempotently, tracking version in `schema_versions`. Fresh DB is created on demand (default path `Extra/database/xiangqi.db`, overridable via `SERVER_DB_PATH` / `SERVER_DB_CONNECTION_STRING`).

## Repository (P1-TV6-D2)

- `MatchRepository.Create` upserts players (`INSERT OR IGNORE`) before inserting match (FK correctness).
- `MoveRepository.TryInsert` uses fully **parameterized SQL**; maps `SQLITE_CONSTRAINT_UNIQUE` (extended code 2067) → duplicate; other constraint failures (e.g. FK) propagate → rollback.
- Both repos only dispose connections they opened themselves; caller-owned connections are left intact (transaction-safe).

## Transaction / Atomic Commit (P1-TV6-D3)

`MoveCommittingService.PersistAtomic`:

1. Opens its own connection, begins a transaction.
2. Repos share that connection.
3. **Persist-first**: inserts the move row (`INSERT INTO moves`).
4. Updates `matches` board state (revision, hashes).
5. Commits.
6. On any non-duplicate failure → **rollback** → `PersistenceFailure`; no partial state, revision/state unchanged.

`board_hash_before` / `board_hash_after` computed via `BoardHasher` and stored on each move.

## Verification

Integration tests in `XiangqiOnline.IntegrationTests/Persistence/` validate schema, FK, unique constraints, atomicity, rollback, and read-back consistency against a real temporary SQLite database.

- `Tv6DatabaseSchemaTests` — schema/table/FK/unique constraint presence
- `Tv6RepositoryTests` — repository behavior, parameterized SQL, disposal
- `Tv6PersistenceIntegrationTests` — end-to-end commit semantics (legal→1 row, duplicate→1 row, rejected→0 rows, persistence-fail→rollback, read-back)
