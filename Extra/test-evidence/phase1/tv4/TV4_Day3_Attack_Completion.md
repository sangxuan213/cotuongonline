# TV4 Phase 1 Day 3 - Attack Map Completion

- Task: P1-TV4-D3
- Branch: `feature/tv4-check-p1`
- Starting commit: `8a246715d43122dc55a9194a4a7019283be9ec96`
- `origin/develop` after fetch: `33ab1d3e2ae8a99585050a0b4046ed19f561073f`
- Rebase required: NO

## Files

- `Code/src/XiangqiOnline.RuleEngine/Attacks/GeneralAttackRule.cs`
- `Code/src/XiangqiOnline.RuleEngine/Attacks/AdvisorAttackRule.cs`
- `Code/src/XiangqiOnline.RuleEngine/Attacks/ElephantAttackRule.cs`
- `Code/src/XiangqiOnline.RuleEngine/Attacks/GeneralsFacingDetector.cs`
- `Code/tests/XiangqiOnline.RuleEngine.Tests/Attacks/GeneralAttackRuleTests.cs`
- `Code/tests/XiangqiOnline.RuleEngine.Tests/Attacks/AdvisorAttackRuleTests.cs`
- `Code/tests/XiangqiOnline.RuleEngine.Tests/Attacks/ElephantAttackRuleTests.cs`
- `Code/tests/XiangqiOnline.RuleEngine.Tests/Attacks/GeneralsFacingDetectorTests.cs`
- `Code/tests/XiangqiOnline.RuleEngine.Tests/Attacks/AttackDetectorCompleteMapTests.cs`
- `Extra/test-evidence/phase1/tv4/TV4_Day3_Attack_Completion.md`

Day 1 and Day 2 production APIs and tests were not changed.

## General attack design

`GeneralAttackRule` first delegates normal one-step orthogonal palace movement and ally-
destination behavior to the existing `GeneralValidator`. If normal validation fails, it
only evaluates flying General when the attacker is a General, the target contains an active
enemy General, and `GeneralsFacingDetector` reports an unobstructed shared file.

A vacant target or an enemy piece that is not a General cannot activate the flying rule.
`GeneralValidator` was not changed because flying General is an attack/check-layer relation,
not normal movement geometry. This preserves the TV3 contract and avoids duplicating its
palace and one-step rules.

The `GeneralAttackRule` constructor rejects a null `GeneralsFacingDetector`.

## GeneralsFacingDetector semantics

`AreGeneralsFacing(BoardState board)` rejects a null board, locates the active Red and Black
Generals, and returns false if either is missing. More than one active General for either
side is treated as malformed state and throws `InvalidOperationException`.

The relation requires equal X coordinates and no active piece strictly between the General
positions. Captured/inactive pieces do not block. It is independent of `BoardState.Turn`
and intentionally does not enforce palace geometry, so columns 0, 4, and 8 are all covered.

## TV3 reuse

- `GeneralAttackRule` delegates normal attacks to `GeneralValidator`.
- `AdvisorAttackRule` delegates palace, diagonal, and ally-target semantics to `AdvisorValidator`.
- `ElephantAttackRule` delegates 2x2 geometry, river, eye, boundary, and ally-target semantics to `ElephantValidator`.

No TV3 validator was modified and no movement geometry was copied into the attack rules.

## Full seven-piece attack map

The complete `AttackDetector` composition registers exactly one rule for each `PieceType`:

- General
- Advisor
- Elephant
- Horse
- Chariot
- Cannon
- Pawn

The initial 32-piece board is evaluated without a missing-rule exception. Integration tests
also prove flying-General detection with and without a blocker.

The Day 2 Cannon behavior is strengthened with a real enemy-General target: Black Cannon
at `(1,2)`, screen at `(1,4)`, and Red General at `(1,6)` produces an attacked result through
`AttackDetector`.

## Tests added

- `GeneralsFacingDetectorTests`: 14 cases
- `GeneralAttackRuleTests`: 12 cases
- `AdvisorAttackRuleTests`: 6 cases
- `ElephantAttackRuleTests`: 9 cases
- `AttackDetectorCompleteMapTests`: 4 cases
- Total Day 3 additions: 45 cases

## Actual build and test result

- Release solution build: PASS
- Build warnings: 0
- Build errors: 0
- RuleEngine tests: 182 passed, 0 failed, 0 skipped (182 total)

## Deferred Day 4

Not implemented:

- Immutable temporary move
- `SelfCheckValidator`
- `CheckDetector`
- Attacker list/check status integration
- `SELF_CHECK`
- `CHECK_NOT_RESOLVED`

Legal move generation, checkmate, stalemate, repetition, and outcome resolution also remain
outside Day 3 scope.
