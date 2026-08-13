# TV4 Phase 1 Test Report

- Phase: P1-TV4
- Branch: `feature/tv4-check-p1`
- Base develop commit: `33ab1d3e2ae8a99585050a0b4046ed19f561073f`
- Day 1 commit: `ee4204cfacfaf6421f0e7c56f45160c1cd1f3a8b`
- Day 2 commit: `8a246715d43122dc55a9194a4a7019283be9ec96`
- Day 3 commit: `ff001b01f23a72d15fe49ea5d2927d2c1a8e50d9`
- Day 4 commit: `c8c82848ecb13f7d05d221a2f5fb83ddd01ef9fd`
- Day 5 commit: the commit containing this report, with message `test(rule): finalize tv4 phase 1 regression and handover`; the resulting hash is recorded in the Day 5 execution report
- .NET SDK: `10.0.302`

## Build result

- Command: `dotnet build Code/XiangqiOnline.sln -c Release`
- Result: PASS
- Warnings: 0
- Errors: 0

## RuleEngine regression

- Command: `dotnet test Code/tests/XiangqiOnline.RuleEngine.Tests/XiangqiOnline.RuleEngine.Tests.csproj -c Release --no-build`
- Total: 229
- Passed: 229
- Failed: 0
- Skipped: 0

TV3 baseline was 83 cases. TV4 Phase 1 adds 146 cases, including 8 Day 5 hardening/regression cases.

## Test groups

- AttackDetector architecture and complete-map integration: 18 cases
- Chariot attack: 7 cases
- Cannon attack: 10 cases
- Horse attack: 11 cases
- Pawn attack: 12 cases
- General attack: 12 cases
- Advisor attack: 6 cases
- Elephant attack: 9 cases
- Generals facing: 14 cases
- CheckDetector, including Red-to-Black and Black-to-Red symmetry: 16 cases
- SelfCheckValidator, invariants, layering, immutability, and regression: 31 cases

These groups total 146 TV4 Phase 1 cases. Existing TV3 tests remain unchanged.

## Full solution test command

- Command: `dotnet test Code/XiangqiOnline.sln -c Release --no-build`
- Exit result: PASS (exit code 0)
- RuleEngine: 229 passed, 0 failed, 0 skipped
- Protocol, Integration, and Server test assemblies reported no discoverable tests; no external test failure occurred

## Day 5 regression additions

- Supplied moving piece Side must match the canonical source piece
- Supplied moving piece Type must match the canonical source piece
- Supplied moving piece must be marked active
- Movement-valid General move into an attacked square returns `SELF_CHECK`
- Black-side blocker removal symmetry returns `SELF_CHECK`
- Removing only one attacker from double check returns `CHECK_NOT_RESOLVED`
- Capture simulation preserves the original checker, mover, and Turn
- `CheckDetector` evaluates Black against a Red attacker
