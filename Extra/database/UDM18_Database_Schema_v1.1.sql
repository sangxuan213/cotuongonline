-- UDM_18 database baseline v1.1
-- Locked by UDM18 Baseline Lock Decisions v1.1
PRAGMA foreign_keys = ON;
PRAGMA journal_mode = WAL;
PRAGMA busy_timeout = 5000;

CREATE TABLE IF NOT EXISTS schema_versions (
  version TEXT PRIMARY KEY,
  applied_at_utc TEXT NOT NULL
);

INSERT OR IGNORE INTO schema_versions(version, applied_at_utc)
VALUES ('1.1', strftime('%Y-%m-%dT%H:%M:%fZ', 'now'));

CREATE TABLE IF NOT EXISTS players (
  player_id TEXT PRIMARY KEY,
  display_name TEXT NOT NULL COLLATE NOCASE UNIQUE,
  created_at_utc TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS matches (
  match_id TEXT PRIMARY KEY,
  room_id TEXT NOT NULL UNIQUE,
  red_player_id TEXT NOT NULL,
  black_player_id TEXT NOT NULL,
  rule_profile_id TEXT NOT NULL,
  rule_profile_version TEXT NOT NULL,
  time_profile TEXT NOT NULL,
  config_json TEXT NOT NULL,
  status TEXT NOT NULL CHECK (status IN ('CREATED','WAITING_FOR_READY','PLAYING','FINISHED','ABORTED_SYSTEM')),
  started_at_utc TEXT NOT NULL,
  ended_at_utc TEXT,
  result_type TEXT CHECK (result_type IS NULL OR result_type IN ('RED_WIN','BLACK_WIN','DRAW','ABORTED')),
  end_reason TEXT,
  winner_side TEXT CHECK (winner_side IS NULL OR winner_side IN ('RED','BLACK')),
  final_revision INTEGER,
  total_moves INTEGER NOT NULL DEFAULT 0,
  FOREIGN KEY(red_player_id) REFERENCES players(player_id),
  FOREIGN KEY(black_player_id) REFERENCES players(player_id)
);

CREATE TABLE IF NOT EXISTS moves (
  move_id TEXT PRIMARY KEY,
  client_move_id TEXT NOT NULL,
  match_id TEXT NOT NULL,
  move_index INTEGER NOT NULL CHECK (move_index >= 1),
  revision INTEGER NOT NULL CHECK (revision >= 1),
  side TEXT NOT NULL CHECK (side IN ('RED','BLACK')),
  piece_id TEXT NOT NULL,
  piece_type TEXT NOT NULL CHECK (piece_type IN ('GENERAL','ADVISOR','ELEPHANT','HORSE','CHARIOT','CANNON','PAWN')),
  from_x INTEGER NOT NULL CHECK (from_x BETWEEN 0 AND 8),
  from_y INTEGER NOT NULL CHECK (from_y BETWEEN 0 AND 9),
  to_x INTEGER NOT NULL CHECK (to_x BETWEEN 0 AND 8),
  to_y INTEGER NOT NULL CHECK (to_y BETWEEN 0 AND 9),
  captured_piece_id TEXT,
  move_class TEXT NOT NULL CHECK (move_class IN ('CHECK','CHASE','KILL','EXCHANGE','BLOCK','OFFER','IDLE')),
  classification_facts_json TEXT NOT NULL DEFAULT '{}',
  is_capture INTEGER NOT NULL CHECK (is_capture IN (0,1)),
  is_check INTEGER NOT NULL CHECK (is_check IN (0,1)),
  is_checkmate INTEGER NOT NULL CHECK (is_checkmate IN (0,1)),
  red_remaining_ms INTEGER NOT NULL CHECK (red_remaining_ms >= 0),
  black_remaining_ms INTEGER NOT NULL CHECK (black_remaining_ms >= 0),
  board_hash_before TEXT NOT NULL,
  board_hash_after TEXT NOT NULL,
  created_at_utc TEXT NOT NULL,
  FOREIGN KEY(match_id) REFERENCES matches(match_id) ON DELETE CASCADE,
  UNIQUE(match_id, move_index),
  UNIQUE(match_id, revision),
  UNIQUE(match_id, client_move_id)
);

CREATE TABLE IF NOT EXISTS position_history (
  match_id TEXT NOT NULL,
  revision INTEGER NOT NULL,
  board_hash TEXT NOT NULL,
  canonical_piece_map_json TEXT NOT NULL,
  side_to_move TEXT NOT NULL CHECK (side_to_move IN ('RED','BLACK')),
  move_class TEXT CHECK (move_class IS NULL OR move_class IN ('CHECK','CHASE','KILL','EXCHANGE','BLOCK','OFFER','IDLE')),
  classification_facts_json TEXT NOT NULL DEFAULT '{}',
  cycle_signature TEXT,
  must_vary_side TEXT CHECK (must_vary_side IS NULL OR must_vary_side IN ('RED','BLACK')),
  adjudication_reason TEXT,
  created_at_utc TEXT NOT NULL,
  PRIMARY KEY(match_id, revision),
  FOREIGN KEY(match_id) REFERENCES matches(match_id) ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS ix_matches_red_started ON matches(red_player_id, started_at_utc DESC);
CREATE INDEX IF NOT EXISTS ix_matches_black_started ON matches(black_player_id, started_at_utc DESC);
CREATE INDEX IF NOT EXISTS ix_matches_status_started ON matches(status, started_at_utc DESC);
CREATE INDEX IF NOT EXISTS ix_moves_match_index ON moves(match_id, move_index);
CREATE INDEX IF NOT EXISTS ix_position_history_hash_side ON position_history(match_id, board_hash, side_to_move);
