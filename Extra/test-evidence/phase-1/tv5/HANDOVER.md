# TV5 Phase 1 — Day 6 handover

Evidence in this directory was regenerated on 2026-08-13 after merging the latest `origin/develop` baseline into `khang`.

## Integration changes

- Preserved the official TV1 protocol, transport and shared model contracts.
- Adapted the client to `SideColor`, `PieceType` and `Position` from `XiangqiOnline.Shared`.
- Removed TV5's obsolete duplicate shared game-contract types.
- Locked the client frame limit to `TcpFrameCodec.MaxPayloadBytes` and used unsigned big-endian frame lengths.
- Added Client, Client SmokeTests and LoadTest to the complete `Code/XiangqiOnline.slnx` build surface.
- Kept TV2 lobby/session, TV3 RuleEngine and TV4 attack/check/self-check implementations unchanged.

## Verified behavior

- WPF MVVM shell starts in normal and demo modes.
- Connection, Lobby and GameRoom navigation remains responsive.
- Board renders 9x10 with 32 stable pieces; board rotation keeps canonical coordinates.
- HELLO precedes LOGIN_REQUEST and the protocol version is `1.0`.
- Client sends authoritative move intent and changes board state only after commit/snapshot.
- Rejected, malformed, unknown-piece and revision-gap events preserve safe state and surface errors/resync.
- Full Release solution build: 0 warnings, 0 errors.
- Combined tests: 339 passed, 0 failed, 0 skipped.

## Scope

- Full reconnect orchestration and clocks are outside TV5 Phase 1 scope.
- The production Server currently accepts TCP connections but does not yet wire the TV2 lobby services to a protocol request dispatcher; a two-client authoritative lobby/game demo therefore remains a server-side integration task, not evidence claimed here.
- No GitHub CI result is claimed.
