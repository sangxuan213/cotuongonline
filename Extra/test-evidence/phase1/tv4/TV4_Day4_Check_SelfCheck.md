# TV4 Phase 1 Day 4 - Check and Self-Check

- Task: P1-TV4-D4
- Branch: `feature/tv4-check-p1`
- Starting commit: `ff001b01f23a72d15fe49ea5d2927d2c1a8e50d9`
- `origin/develop` after fetch: `33ab1d3e2ae8a99585050a0b4046ed19f561073f`
- Rebase required: NO

## Files

- `Code/src/XiangqiOnline.RuleEngine/Checks/CheckStatus.cs`
- `Code/src/XiangqiOnline.RuleEngine/Checks/CheckDetector.cs`
- `Code/src/XiangqiOnline.RuleEngine/Checks/SelfCheckValidator.cs`
- `Code/tests/XiangqiOnline.RuleEngine.Tests/Checks/CheckTestFactory.cs`
- `Code/tests/XiangqiOnline.RuleEngine.Tests/Checks/CheckDetectorTests.cs`
- `Code/tests/XiangqiOnline.RuleEngine.Tests/Checks/SelfCheckValidatorTests.cs`
- `Extra/test-evidence/phase1/tv4/TV4_Day4_Check_SelfCheck.md`

No Day 1-3 production code or tests were changed.

## CheckStatus API

```csharp
public sealed record CheckStatus(
    SideColor CheckedSide,
    Position GeneralPosition,
    IReadOnlyList<PieceState> CheckingPieces)
{
    public bool IsInCheck => CheckingPieces.Count > 0;
}
```

`CheckingPieces` contains every attacker returned by `AttackDetector.FindAttackers` and
therefore retains its deterministic ordinal Piece ID ordering. Check status is a RuleEngine
domain fact and remains outside Shared.

## CheckDetector API and behavior

```csharp
public CheckDetector(AttackDetector attackDetector);
public CheckStatus Evaluate(BoardState board, SideColor side);
```

The constructor and `Evaluate` reject null dependencies/input. Evaluation requires exactly
one active General for the checked side; a missing or duplicate General throws
`InvalidOperationException`. The opponent is derived locally as Red to Black or Black to
Red without changing `SideColor`.

The detector asks the injected `AttackDetector` for all opponents attacking the General
position. It contains no piece-specific rules, does not read `BoardState.Turn`, does not
mutate the board, and does not invoke movement, self-check, or legal-move validation.

## Temporary move and input immutability

`SelfCheckValidator` reuses the existing immutable primitive:

```csharp
var temporaryBoard = board.ApplyMove(movingPiece.Position, target);
```

There is no second board-cloning implementation. Tests prove that validation preserves the
original board's `Pieces` instance, original `Turn`, source occupancy, and empty target while
the temporary board alone is evaluated. The Turn change made by `ApplyMove` is irrelevant
because check and attack detection are Turn-independent.

## SelfCheckValidator API and semantics

```csharp
public SelfCheckValidator(
    CheckDetector checkDetector,
    GeneralsFacingDetector generalsFacingDetector);

public MoveValidationResult Validate(
    BoardState board,
    PieceState movingPiece,
    Position target);
```

Null dependencies/input and out-of-board targets are rejected. The moving Piece ID must
match the active source piece; violation is an `InvalidOperationException` because the
movement stage is expected to have completed first.

The validator records pre-move check state, applies the immutable temporary move, and then
uses this mandatory precedence:

1. `GENERALS_FACING` when the temporary board exposes the two Generals on an open file.
2. Success when the moving side is not checked after the move.
3. `CHECK_NOT_RESOLVED` when it was checked before and remains checked.
4. `SELF_CHECK` when the move newly exposes or moves its General into attack.

Coverage includes exposing a Chariot line, unresolved check, moving the General away,
capturing a checker, blocking a line, removing/creating a Cannon screen, unblocking a Horse
leg, exposing Generals, and resolving flying-General check by inserting a blocker.

## Movement-valid is not necessarily legal

Two layering tests call the existing `MoveValidationPipeline` first. One Chariot move passes
TV3 movement geometry but returns `SELF_CHECK` from `SelfCheckValidator`; another passes both
movement and king-safety validation. The pipeline was not modified and self-check does not
repeat horse, cannon, palace, elephant, pawn, Chariot, or turn rules.

## No recursion

The dependency direction is one-way:

`SelfCheckValidator -> CheckDetector -> AttackDetector -> IAttackRule`

Neither `AttackDetector` nor any attack rule references `CheckDetector`, `SelfCheckValidator`,
or legal move generation. `CheckDetector` only receives an already composed AttackDetector;
it does not create the seven rules internally.

## Tests added

- `CheckDetectorTests`: 15 cases
- `SelfCheckValidatorTests`: 24 cases
- Total Day 4 additions: 39 cases
- Includes two movement-plus-king-safety layering cases

## Actual build and test result

- Release solution build: PASS
- Build warnings: 0
- Build errors: 0
- RuleEngine tests: 221 passed, 0 failed, 0 skipped (221 total)

## Deferred Day 5

Not implemented:

- Final regression/evidence/handover
- Phase 1 pull request
- TV3 review
- Integration/gate work

No Phase 1 final-pass claim is made here. Checkmate, stalemate, repetition, and outcome
resolution also remain outside Day 4 scope.
