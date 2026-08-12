# TV3 Phase 1 - Rule Engine Core Test Report

## 1. Executive Summary
- **Module**: `XiangqiOnline.RuleEngine` & `XiangqiOnline.Shared`
- **Target Framework**: .NET 10
- **Status**: READY_FOR_TV4_REVIEW
- **Pass Rate**: 100% (83 / 83 tests passed)
- **Failed**: 0
- **Skipped**: 0

## 2. Test Execution Details by Piece Type

| Piece Type / Subsystem | Required Min Tests | Actual Test Count | Passed | Failed | Skipped | Status |
| :--- | :---: | :---: | :---: | :---: | :---: | :--- |
| **GENERAL** | >= 8 | 8 | 8 | 0 | 0 | PASS |
| **ADVISOR** | >= 6 | 6 | 6 | 0 | 0 | PASS |
| **ELEPHANT** | >= 10 | 10 | 10 | 0 | 0 | PASS |
| **HORSE** | >= 12 | 12 | 12 | 0 | 0 | PASS |
| **CHARIOT** | >= 10 | 10 | 10 | 0 | 0 | PASS |
| **CANNON** | >= 14 | 14 | 14 | 0 | 0 | PASS |
| **PAWN** | >= 10 | 10 | 10 | 0 | 0 | PASS |
| **BoardState & Pipeline** | - | 13 | 13 | 0 | 0 | PASS |
| **TOTAL** | **>= 70** | **83** | **83** | **0** | **0** | **ALL PASSED** |

## 3. Canonical Verification & Contract Audits

- **Canonical Coordinates**: `X in [0..8]`, `Y in [0..9]`.
  - `BLACK`: Top (`Y = 0`), moves `+Y`. Palace `X in [3..5], Y in [0..2]`. Uncrossed river `Y <= 4`.
  - `RED`: Bottom (`Y = 9`), moves `-Y`. Palace `X in [3..5], Y in [7..9]`. Uncrossed river `Y >= 5`.
- **Canonical Piece Identifiers (32 Pieces)**:
  - `BLACK_CHARIOT_1`, `BLACK_HORSE_1`, `BLACK_ELEPHANT_1`, `BLACK_ADVISOR_1`, `BLACK_GENERAL`, `BLACK_ADVISOR_2`, `BLACK_ELEPHANT_2`, `BLACK_HORSE_2`, `BLACK_CHARIOT_2`
  - `BLACK_CANNON_1`, `BLACK_CANNON_2`, `BLACK_PAWN_1`..`BLACK_PAWN_5`
  - `RED_CHARIOT_1`, `RED_HORSE_1`, `RED_ELEPHANT_1`, `RED_ADVISOR_1`, `RED_GENERAL`, `RED_ADVISOR_2`, `RED_ELEPHANT_2`, `RED_HORSE_2`, `RED_CHARIOT_2`
  - `RED_CANNON_1`, `RED_CANNON_2`, `RED_PAWN_1`..`RED_PAWN_5`
- **Shared Enums & DTOs**:
  - `PieceType.Chariot` (renamed from `Rook`).
  - `MoveIntent(string ClientMoveId, Position From, Position To, long ExpectedRevision)`.
  - `ErrorCodes` string values without `ERR_` prefixes (`HORSE_LEG_BLOCKED`, `ELEPHANT_EYE_BLOCKED`, `ELEPHANT_CROSSES_RIVER`, `CANNON_SCREEN_INVALID`, `PAWN_RETREATS`, `OUTSIDE_PALACE`, `ALLY_AT_DESTINATION`, `INVALID_GEOMETRY`, `PATH_BLOCKED`, `NO_PIECE_AT_SOURCE`, `NOT_YOUR_TURN`, `OUT_OF_BOARD`).
  - **Lưu ý**: Danh sách `ErrorCodes` trên chỉ liệt kê tập con di chuyển (movement subset) của TV3. Các mã do `MoveValidationPipeline` / `SelfCheckValidator` trả về (`INTERNAL_SERVER_ERROR`, `GENERALS_FACING`, `CHECK_NOT_RESOLVED`, `SELF_CHECK`) được triển khai ở giai đoạn TV4.
