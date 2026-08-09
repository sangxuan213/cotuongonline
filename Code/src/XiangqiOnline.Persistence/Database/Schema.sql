-- ============================================================================
-- XiangqiOnline Persistence Schema (TV6 Phase 1)
-- SQLite-compatible DDL. Fresh database migration.
-- Includes: schema_versions, players, matches, moves, position_history
-- Foreign keys enabled via connection string (ForeignKeys=true).
-- ============================================================================

PRAGMA foreign_keys = ON;

-- ----------------------------------------------------------------------------
-- schema_versions: theo doi phien ban schema da ap dung (idempotent migration)
-- ----------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS schema_versions (
    version       INTEGER PRIMARY KEY,
    applied_at_utc TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ', 'now')),
    description    TEXT NOT NULL DEFAULT ''
);

-- ----------------------------------------------------------------------------
-- players: nguoi choi (dinh danh ben ngoai)
-- ----------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS players (
    player_id    TEXT PRIMARY KEY,          -- external id (ULID)
    display_name TEXT NOT NULL DEFAULT '',
    created_at_utc TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ', 'now'))
);

-- ----------------------------------------------------------------------------
-- matches: tran dau / van co
-- ----------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS matches (
    match_id       TEXT PRIMARY KEY,
    white_player_id TEXT,                    -- FK -> players.player_id (nullable)
    black_player_id TEXT,                    -- FK -> players.player_id (nullable)
    status          TEXT NOT NULL DEFAULT 'PLAYING',
    current_turn    TEXT NOT NULL DEFAULT 'RED',   -- 'RED' | 'BLACK'
    revision        INTEGER NOT NULL DEFAULT 0,
    board_hash      TEXT NOT NULL DEFAULT '',
    created_at_utc  TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ', 'now')),
    updated_at_utc  TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ', 'now')),

    CONSTRAINT fk_matches_white FOREIGN KEY (white_player_id) REFERENCES players(player_id) ON DELETE SET NULL,
    CONSTRAINT fk_matches_black FOREIGN KEY (black_player_id) REFERENCES players(player_id) ON DELETE SET NULL
);

-- ----------------------------------------------------------------------------
-- moves: nuoc di da duoc commit (persist-first)
-- Moi (match_id, client_move_id) phai la DUY NHAT -> bao ve duplicate retry.
-- ----------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS moves (
    move_id           TEXT PRIMARY KEY,      -- internal id (ULID)
    match_id          TEXT NOT NULL,
    client_move_id    TEXT NOT NULL,         -- idempotency key tu client
    piece_id          TEXT NOT NULL,
    from_x            INTEGER NOT NULL,
    from_y            INTEGER NOT NULL,
    to_x              INTEGER NOT NULL,
    to_y              INTEGER NOT NULL,
    captured_piece_id TEXT NULL,
    board_hash_before TEXT NOT NULL DEFAULT '',
    board_hash_after  TEXT NOT NULL DEFAULT '',
    move_number       INTEGER NOT NULL,
    result            TEXT NOT NULL DEFAULT 'COMMITTED',   -- 'COMMITTED' | 'REJECTED' | ...
    created_at_utc    TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ', 'now')),

    CONSTRAINT fk_moves_match FOREIGN KEY (match_id) REFERENCES matches(match_id) ON DELETE CASCADE,
    CONSTRAINT uq_moves_match_client UNIQUE (match_id, client_move_id)
);

-- ----------------------------------------------------------------------------
-- position_history: lich su vi tri quan co (audit / replay / read-back)
-- ----------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS position_history (
    history_id   TEXT PRIMARY KEY,
    match_id     TEXT NOT NULL,
    move_id      TEXT NOT NULL,
    piece_id     TEXT NOT NULL,
    x            INTEGER NOT NULL,
    y            INTEGER NOT NULL,
    is_alive     INTEGER NOT NULL DEFAULT 1,
    recorded_at_utc TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ', 'now')),

    CONSTRAINT fk_position_history_match FOREIGN KEY (match_id) REFERENCES matches(match_id) ON DELETE CASCADE,
    CONSTRAINT fk_position_history_move  FOREIGN KEY (move_id)  REFERENCES moves(move_id) ON DELETE CASCADE
);

-- ----------------------------------------------------------------------------
-- Indexes cho truy van thuong gap
-- ----------------------------------------------------------------------------
CREATE INDEX IF NOT EXISTS idx_moves_match_id        ON moves(match_id);
CREATE INDEX IF NOT EXISTS idx_moves_client_move_id  ON moves(client_move_id);
CREATE INDEX IF NOT EXISTS idx_matches_status        ON matches(status);
CREATE INDEX IF NOT EXISTS idx_position_history_match ON position_history(match_id);
CREATE INDEX IF NOT EXISTS idx_position_history_move  ON position_history(move_id);

-- ----------------------------------------------------------------------------
-- Seed schema version 1
-- ----------------------------------------------------------------------------
INSERT OR IGNORE INTO schema_versions (version, description) VALUES (1, 'TV6 Phase 1 baseline schema');
