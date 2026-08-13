# TV4 Phase 1 Handover

## Ownership

- Owner: TV4
- Required reviewer: TV3

## Scope completed

- `AttackDetector` architecture
- Seven `IAttackRule` implementations
- `GeneralsFacingDetector`
- `CheckStatus`
- `CheckDetector`
- `SelfCheckValidator`
- Temporary immutable simulation via `BoardState.ApplyMove`
- `SELF_CHECK`
- `CHECK_NOT_RESOLVED`
- `GENERALS_FACING`

## Public APIs

```csharp
public interface IAttackRule
{
    PieceType MatchingPieceType { get; }
    bool CanAttack(BoardState board, PieceState attacker, Position target);
}

public sealed class AttackDetector
{
    public AttackDetector(IEnumerable<IAttackRule> rules);
    public bool IsSquareAttacked(BoardState board, Position target, SideColor attackingSide);
    public IReadOnlyList<PieceState> FindAttackers(BoardState board, Position target, SideColor attackingSide);
}

public sealed class GeneralsFacingDetector
{
    public bool AreGeneralsFacing(BoardState board);
}

public sealed class CheckDetector
{
    public CheckDetector(AttackDetector attackDetector);
    public CheckStatus Evaluate(BoardState board, SideColor side);
}

public sealed class SelfCheckValidator
{
    public SelfCheckValidator(CheckDetector checkDetector, GeneralsFacingDetector generalsFacingDetector);
    public MoveValidationResult Validate(BoardState board, PieceState movingPiece, Position target);
}
```

`CheckStatus` reports the checked side, General position, deterministic checking-piece list,
and derived `IsInCheck` value.

## Dependency direction

```text
SelfCheckValidator
  -> CheckDetector
    -> AttackDetector
      -> IAttackRule
```

There is no dependency back from the attack layer to check, self-check, or legal-move logic.

## TV3 reuse

- `GeneralValidator`, `AdvisorValidator`, `ElephantValidator`
- `HorseValidator`, `ChariotValidator`, `PawnValidator`
- `BoardState` and `BoardState.ApplyMove`
- `PieceState`
- `Position`
- `BoardSetupFixture`

Cannon attack uses a small attack-specific screen count because capture-threat semantics
require exactly one screen, unlike normal empty-target Cannon movement. Flying General stays
in the attack/check layer and is not added to `GeneralValidator`.

## Usage by another module

1. Run `MoveValidationPipeline.Validate` first.
2. Only when movement validation passes, run `SelfCheckValidator.Validate`.
3. Use `CheckDetector.Evaluate` for check status/notification and checking-piece details.

Do not use `AttackDetector` as a legal-move validator. The caller/composition root must
construct `AttackDetector` with all seven rules and inject it into `CheckDetector`.

## Not implemented

- Checkmate
- Stalemate
- Full legal move generation
- Repetition
- WXF adjudication
- Result/outcome resolver

## Running verification

```powershell
dotnet build Code/XiangqiOnline.sln -c Release
dotnet test Code/tests/XiangqiOnline.RuleEngine.Tests/XiangqiOnline.RuleEngine.Tests.csproj -c Release --no-build
dotnet test Code/XiangqiOnline.sln -c Release --no-build
```

Expected RuleEngine result for this handover: 229 passed, 0 failed, 0 skipped.

## Known risks and notes

- Downstream composition/integration remains pending and must register all seven attack rules.
- `SelfCheckValidator` is a post-movement stage; calling it without prior movement validation is misuse.
- Protocol, Integration, and Server test assemblies currently report no discoverable tests in the full-solution command; this is not a TV4 test failure.
- PR review, merge, and post-merge integration regression remain pending.

## Contract statement

No Shared Contract, protocol, enum, error-code, framework, Client, or Server change is included.
