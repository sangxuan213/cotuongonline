## Tasks

- P1-TV4-D1
- P1-TV4-D2
- P1-TV4-D3
- P1-TV4-D4
- P1-TV4-D5

## Summary

Completes TV4 Phase 1 RuleEngine attack and check validation: attack orchestration, all seven
piece attack rules, flying-General detection, deterministic check status and attacker lists,
and immutable post-movement self-check validation with explicit error precedence.

## Architecture

```text
MoveValidationPipeline (TV3 movement stage)
  -> SelfCheckValidator
    -> CheckDetector
      -> AttackDetector
        -> IAttackRule
```

`SelfCheckValidator` reuses `BoardState.ApplyMove` for temporary immutable simulation.
Attack detection does not depend on check, self-check, or legal-move generation, preventing
recursion. Flying General remains in the TV4 attack/check layer rather than TV3 movement.

## Files/modules

- `XiangqiOnline.RuleEngine/Attacks`: attack contract, detector, seven rules, generals-facing relation
- `XiangqiOnline.RuleEngine/Checks`: `CheckStatus`, `CheckDetector`, `SelfCheckValidator`
- RuleEngine attack/check tests
- Phase 1 evidence, handover, and PR body

## Tests

- Release solution build: PASS, 0 warnings, 0 errors
- RuleEngine: 229 passed, 0 failed, 0 skipped
- TV3 baseline: 83 unchanged cases
- TV4 Phase 1: 146 cases, including 8 Day 5 hardening/regression cases
- Full-solution test command: exit code 0; RuleEngine 229 passed

## Manual verification

- Audited all TV4 production Attack and Check sources
- No TODO/FIXME/HACK acceptance debt
- No Socket, WPF, SQLite, Server, or Client dependency
- No `MoveValidationPipeline` dependency in Attack/Check production
- No attack-to-check/self-check dependency or legal-move recursion
- `CheckDetector` does not internally compose seven rules
- `SelfCheckValidator` does not repeat TV3 movement validation
- Checking-piece order remains deterministic by ordinal Piece ID
- `GeneralValidator` remains unchanged and contains no flying-General rule
- No hidden mutable static state

## Contract

- No Shared Contract change
- No protocol change
- No enum or ErrorCodes change
- No framework change

## Deferred

- Checkmate
- Stalemate
- Full legal move generation
- Repetition
- WXF adjudication
- Result/outcome resolver
- Phase 2 features

## Reviewer

TV3 review is required before merge. Reviewer assignment must be performed manually if the
TV3 GitHub username is not known.

## Risks

- Downstream composition and integration gates remain pending.
- Ancillary Protocol, Integration, and Server test assemblies currently contain no discoverable tests.

Do not merge until TV3 review and required integration gates are complete.
