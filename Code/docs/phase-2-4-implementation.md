# UDM18 Phase 2-4 implementation map

Target runtime: .NET 10 (`global.json` 10.0.100 with latest-feature roll-forward).

## Phase 2 - complete core match

- `ServerClock`: monotonic elapsed time, four locked profiles, increment only after accepted move.
- `GameRoom`: serialized mutation gate, clock, move facts, draw state and exactly-once result.
- `LegalMoveGenerator` and `GameTerminationDetector`: checkmate/no-legal-move resolution.
- Server routes: `PING`, `CHALLENGE_CANCEL`, `RESIGN_REQUEST`, `DRAW_OFFER`, `DRAW_RESPONSE`.
- Persistence: match completion, player history list, match detail and replay position.
- WPF: Server clocks, resign/draw controls and terminal result feedback.

## Phase 3 - reconnect, spectator and layer-2 rules

- 256-bit resume token; only SHA-256 hash is retained in the session.
- 60-second reconnect state, seat validation, atomic snapshot and revision-gap resync.
- Active match directory; spectator join/leave and realtime room fan-out.
- Server authorization rejects spectator mutations.
- Move classification facts, exact-board cycle detection, must-vary warning and repetition result.
- Position history remains the audit source for replay; Client never recalculates a result.

## Phase 4 - hardening and release

- Per-socket write serialization, bounded request-id cache and 40 request/second isolation.
- Protocol version/schema checks, graceful cancellation and spectator cleanup.
- Deterministic rule/hash/repetition tests and reconnect/clock/result tests.
- Real TCP load tool with Load A/Load B profiles and JSON/CSV percentile reports.
- Config validation and clean build/test commands are documented in the repository README.

## Wire compatibility

The locked Phase 1 schema and `REVISION_MISMATCH` response are preserved for regression compatibility. New routes and payload fields are additive under protocol version 1.0.
