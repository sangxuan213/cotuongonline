# TV4 Phase 1 Day 1 - Attack Architecture

- Task ID: P1-TV4-D1
- Branch: `feature/tv4-check-p1`
- Starting commit: `33ab1d3`
- .NET SDK baseline: `10.0.302`
- TV3 baseline: 83/83 tests passed

## Architecture boundaries

Movement is the piece geometry and its basic constraints: palace and river boundaries,
path blocking, horse legs, elephant eyes, cannon screens, and pawn direction. TV3 owns
those rules.

Attack asks whether a piece of the attacking side threatens to capture an enemy piece on
a target square according to that piece's attack/capture rule. It is independent of
`BoardState.Turn`, expected revision, sessions, rooms, and the attacker's own self-check.

A legal move is a higher-level operation combining state/server prerequisites, movement
rules, a temporary move, the moving side's general safety, and the generals-facing
constraint. Therefore attack is not the same as a legal move.

Check means that a side's current general square is attacked by the opposing side. Day 1
only establishes the attack architecture needed by a later `CheckDetector`; it does not
implement check detection.

`AttackDetector` does not call `MoveValidationPipeline`, `IMoveValidator`, legal move
generation, self-check validation, or check detection. A future legal-move validator will
depend on attack detection to determine self-check. Making attack detection depend on
legal-move validation would create the circular flow AttackDetector -> legal move/self-check
-> AttackDetector. Dispatching directly to `IAttackRule` prevents this recursion.

## Public API

`IAttackRule` exposes:

```csharp
PieceType MatchingPieceType { get; }
bool CanAttack(BoardState board, PieceState attacker, Position target);
```

`AttackDetector` exposes:

```csharp
AttackDetector(IEnumerable<IAttackRule> rules);
bool IsSquareAttacked(BoardState board, Position target, SideColor attackingSide);
IReadOnlyList<PieceState> FindAttackers(BoardState board, Position target, SideColor attackingSide);
```

The detector maps one rule per piece type, iterates `BoardState.GetActivePieces(attackingSide)`,
dispatches by `PieceState.Type`, and returns attackers ordered by `PieceState.Id` using
`StringComparer.Ordinal`. Duplicate rules, missing rules for active pieces, and targets
outside the canonical 9x10 board fail fast.

## TV3 reuse

The implementation reuses `BoardState`, `PieceState`, `Position`, `SideColor`, `PieceType`,
and `BoardState.GetActivePieces(...)`. Tests reuse `BoardSetupFixture`. No TV3 movement
logic or fixture was duplicated or modified.

## Files created

- `Code/src/XiangqiOnline.RuleEngine/Attacks/IAttackRule.cs`
- `Code/src/XiangqiOnline.RuleEngine/Attacks/AttackDetector.cs`
- `Code/tests/XiangqiOnline.RuleEngine.Tests/Attacks/AttackDetectorTests.cs`
- `Extra/test-evidence/phase1/tv4/TV4_Day1_Attack_Architecture.md`

## Tests added

Eleven test methods (14 test cases, including four out-of-board theory cases) cover positive and negative detection, turn independence, side and
active-piece filtering, piece-type dispatch, all-attacker collection, ordinal deterministic
ordering, duplicate registration, missing-rule failure, and out-of-board targets. The
tests use a private predicate-based orchestration double and contain no piece geometry.

## Actual build and test result

- Release solution build: PASS
- Build warnings: 0
- RuleEngine tests: 97 passed, 0 failed, 0 skipped (97 total)

## Deferred

### Day 2

- Chariot attack
- Cannon attack
- Horse attack
- Pawn attack

### Day 3

- General attack
- Advisor attack
- Elephant attack
- `GeneralsFacingDetector`

### Day 4

- Immutable temporary move
- `SelfCheckValidator`
- `CheckDetector`
- Attacker list integration
- `SELF_CHECK`
- `CHECK_NOT_RESOLVED`

None of the deferred Day 2, Day 3, or Day 4 behavior is implemented in Day 1.
