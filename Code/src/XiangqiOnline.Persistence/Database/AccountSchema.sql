-- Additive account migration v1.2. Keep Schema.sql v1.1 checksum locked.
PRAGMA foreign_keys = ON;

INSERT OR IGNORE INTO schema_versions(version, applied_at_utc)
VALUES ('1.2-accounts', strftime('%Y-%m-%dT%H:%M:%fZ', 'now'));

CREATE TABLE IF NOT EXISTS accounts (
  account_id TEXT PRIMARY KEY,
  email TEXT NOT NULL COLLATE NOCASE UNIQUE,
  display_name TEXT NOT NULL COLLATE NOCASE UNIQUE,
  password_hash BLOB NOT NULL,
  password_salt BLOB NOT NULL,
  password_iterations INTEGER NOT NULL CHECK (password_iterations >= 100000),
  is_active INTEGER NOT NULL DEFAULT 1 CHECK (is_active IN (0,1)),
  created_at_utc TEXT NOT NULL,
  updated_at_utc TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS password_reset_codes (
  reset_id TEXT PRIMARY KEY,
  account_id TEXT NOT NULL,
  code_hash BLOB NOT NULL,
  expires_at_utc TEXT NOT NULL,
  consumed_at_utc TEXT,
  attempt_count INTEGER NOT NULL DEFAULT 0,
  requested_at_utc TEXT NOT NULL,
  FOREIGN KEY(account_id) REFERENCES accounts(account_id) ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS ix_password_reset_account_requested ON password_reset_codes(account_id, requested_at_utc DESC);
CREATE INDEX IF NOT EXISTS ix_password_reset_expiry ON password_reset_codes(expires_at_utc);
