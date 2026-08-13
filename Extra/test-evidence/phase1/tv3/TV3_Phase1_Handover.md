# TV3 Phase 1 - Rule Engine Handover Document for TV4

## 0. Baseline Snapshot
> **Lưu ý**: Tài liệu này mô tả lịch sử baseline tại commit `051b125`
> (nhánh `feature/tv3-ruleengine-p1`, khi **AttackDetector / SelfCheck chưa được triển khai**).
> Trên nhánh `develop` hiện tại, các tính năng TV4 (`AttackDetector`, `SelfCheckValidator`,
> `CheckDetector`, `GeneralsFacingDetector`) **đã được implement**; vui lòng tham chiếu
> `Extra/test-evidence/phase1/tv4/*` để xem trạng thái mới nhất.

## 1. Handover Overview
- **Owner**: TV3 (Rule Engine Core)
- **Target Reviewer**: TV4
- **Branch**: `feature/tv3-ruleengine-p1`
- **Common Base**: `origin/develop` @ `051b125`
- **Status**: READY_FOR_TV4_REVIEW

## 2. Shared APIs Available for TV4 Phase 2

TV4 can consume the following stable abstractions from TV3:

1. **`BoardSetupFixture`** (`XiangqiOnline.RuleEngine.Tests.Fixtures.BoardSetupFixture`):
   - `BoardSetupFixture.CreateBoardWithPieces(SideColor turn, params PieceState[] pieces)`: Allows TV4 to construct arbitrary custom test board states for Attack, Check, and Checkmate testing.
   - `BoardSetupFixture.CreateBoardWithGenerals(SideColor turn)`: Sets up a board with just both Generals in their canonical Palaces.

2. **`BoardState`** (`XiangqiOnline.RuleEngine.Models.BoardState`):
   - Immutable record representation of the 9x10 Xiangqi board.
   - `CreateInitialBoard(SideColor turn)`: Creates standard 32-piece initial board.
   - `GetPieceAt(Position pos)`: Gets piece state at coordinate (null if empty or captured).
   - `GetActivePieces(SideColor side)`: Returns live active pieces for a specific color.
   - `ApplyMove(Position from, Position to)`: Returns a new immutable `BoardState` with updated position and switched turn.

3. **`MoveValidationPipeline`** (`XiangqiOnline.RuleEngine.Pipeline.MoveValidationPipeline`):
   - 7-stage deterministic movement pipeline returning `MoveValidationResult`.
   - Business errors return catalog error codes without throwing runtime exceptions.

## 3. Scope Compliance Audit

- **Framework modified**: NO (.NET 10 solution build intact).
- **Client / Server / DB modified**: NO.
- **TV4 features implemented (AttackDetector / SelfCheck)**: NO — tại baseline `051b125`. (Đã implement trên `develop` từ TV4.)
- **Phase 2 / Phase 3 added**: NO.
