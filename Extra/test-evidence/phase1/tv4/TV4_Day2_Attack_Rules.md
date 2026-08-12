# TV4 Phase 1 Day 2 - Primary Piece Attack Rules

- Task: P1-TV4-D2
- Branch: `feature/tv4-check-p1`
- Starting commit: `ee4204cfacfaf6421f0e7c56f45160c1cd1f3a8b`

## Files

- `Code/src/XiangqiOnline.RuleEngine/Attacks/ChariotAttackRule.cs`
- `Code/src/XiangqiOnline.RuleEngine/Attacks/CannonAttackRule.cs`
- `Code/src/XiangqiOnline.RuleEngine/Attacks/HorseAttackRule.cs`
- `Code/src/XiangqiOnline.RuleEngine/Attacks/PawnAttackRule.cs`
- `Code/tests/XiangqiOnline.RuleEngine.Tests/Attacks/ChariotAttackRuleTests.cs`
- `Code/tests/XiangqiOnline.RuleEngine.Tests/Attacks/CannonAttackRuleTests.cs`
- `Code/tests/XiangqiOnline.RuleEngine.Tests/Attacks/HorseAttackRuleTests.cs`
- `Code/tests/XiangqiOnline.RuleEngine.Tests/Attacks/PawnAttackRuleTests.cs`
- `Extra/test-evidence/phase1/tv4/TV4_Day2_Attack_Rules.md`

Day 1 `IAttackRule`, `AttackDetector`, and their tests were not changed.

## Rule semantics

### Chariot

Attacks a different square on the same rank or file when no active piece lies between
source and target. A diagonal, same-square target, blocked path, out-of-board target, or
ally-occupied target is not attacked.

### Cannon

Attacks a different square on the same rank or file only when exactly one active piece
lies strictly between source and target. The screen can belong to either side. Zero,
two, or more screens, diagonal and same-square targets, out-of-board targets, and
ally-occupied targets return false.

### Horse

Attacks using `(1,2)` or `(2,1)` absolute delta when the corresponding orthogonal horse
leg is empty. Invalid geometry, a blocked leg, an out-of-board target, or an ally-occupied
target returns false.

### Pawn

Black attacks one step toward `+Y`; Red attacks one step toward `-Y`. Before crossing the
river, only the forward square is controlled. After crossing, either adjacent horizontal
square is also controlled. Pawns never attack backward or diagonally and do not attack an
ally-occupied target. River status uses `Position.HasCrossedRiver` through the existing
TV3 pawn validation behavior.

All four rules are independent of `BoardState.Turn`, session, revision, room, self-check,
and general safety.

## TV3 reuse and attack-specific behavior

Chariot, Horse, and Pawn attack geometry, blocking, board-boundary, and ally-target
semantics are fully equivalent to their TV3 movement validation. Their attack rules
delegate to `ChariotValidator`, `HorseValidator`, and `PawnValidator` respectively and use
the returned `IsValid` result. This avoids duplicating TV3 movement logic.

Cannon deliberately does not reuse `CannonValidator` because its non-capture branch accepts
an empty target with zero screens as a normal move. An attack/control query requires capture-
threat semantics: exactly one screen even when the queried target square is empty. The
attack-specific implementation only performs target validation, ally filtering, orthogonal
geometry, and active-screen counting. It does not implement or invoke legal-move validation.

Canonical proof case covered by tests:

- Black Cannon source `(1,2)`
- One active screen `(1,4)`
- Target `(1,6)`
- Result: `true`

The same source and target with zero screens returns `false`; screens at both `(1,3)` and
`(1,4)` also return `false`. This demonstrates attack is not Cannon non-capture movement.

## Tests added

- Chariot: 7 cases
- Cannon: 10 cases
- Horse: 11 cases
- Pawn: 12 cases
- Total Day 2 additions: 40 cases

Coverage includes clear and blocked paths, exact cannon screen counts and screen ownership,
horse orientations and both leg directions, canonical pawn direction/river behavior,
same-square and invalid geometry, edge-board behavior, and ally-occupied targets.

## Actual build and test result

- Release solution build: PASS
- Build warnings: 0
- Build errors: 0
- RuleEngine tests: 137 passed, 0 failed, 0 skipped (137 total)

## Deferred Day 3 and Day 4

Not implemented:

- `GeneralAttackRule`
- `AdvisorAttackRule`
- `ElephantAttackRule`
- `GeneralsFacingDetector`
- `CheckDetector`
- `SelfCheckValidator`
- Immutable temporary move
- Legal move generation
- `SELF_CHECK`
- `CHECK_NOT_RESOLVED`
- Checkmate/stalemate
