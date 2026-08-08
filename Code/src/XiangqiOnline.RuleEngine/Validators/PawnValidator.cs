using XiangqiOnline.RuleEngine.Models;
using XiangqiOnline.Shared.Enums;
using XiangqiOnline.Shared.Models;

namespace XiangqiOnline.RuleEngine.Validators;

/// <summary>
/// Validator kiểm tra luật di chuyển cho Tốt (Pawn).
/// Quy tắc Canonical:
/// - BLACK ở trên (y=0), tiến theo +Y (forwardDy = dy).
/// - RED ở dưới (y=9), tiến theo -Y (forwardDy = -dy).
/// - Tốt KHÔNG ĐƯỢC ĐI LÙI (forwardDy < 0 -> PAWN_RETREATS).
/// - Trước khi qua sông: Tốt chỉ được tiến 1 ô về phía trước (forwardDy == 1 && dx == 0).
/// - Sau khi qua sông: Tốt được tiến 1 ô (forwardDy == 1 && dx == 0) HOẶC đi ngang 1 ô (forwardDy == 0 && |dx| == 1).
/// </summary>
public class PawnValidator : IMoveValidator
{
    public PieceType MatchingPieceType => PieceType.Pawn;

    public MoveValidationResult Validate(BoardState board, PieceState piece, Position to)
    {
        if (!to.IsValid())
        {
            return MoveValidationResult.Fail(ErrorCodes.OUT_OF_BOARD, "Tọa độ đích nằm ngoài bàn cờ.");
        }

        // 1. Kiểm tra ô đích có quân đồng minh không
        var targetPiece = board.GetPieceAt(to);
        if (targetPiece != null && targetPiece.Side == piece.Side)
        {
            return MoveValidationResult.Fail(ErrorCodes.ALLY_AT_DESTINATION, "Không thể đi vào ô đã có quân đồng minh.");
        }

        int dx = to.X - piece.Position.X;
        int dy = to.Y - piece.Position.Y;
        int absDx = Math.Abs(dx);

        // Canonical forward direction: BLACK is at top (y=0) going +Y, RED is at bottom (y=9) going -Y
        int forwardDy = (piece.Side == SideColor.Black) ? dy : -dy;

        // 2. Tốt không được đi lùi
        if (forwardDy < 0)
        {
            return MoveValidationResult.Fail(ErrorCodes.PAWN_RETREATS, "Tốt không được đi lùi.");
        }

        bool crossedRiver = piece.Position.HasCrossedRiver(piece.Side);

        // 3. Trước khi qua sông: Chỉ tiến 1 ô (forwardDy == 1 && dx == 0)
        if (!crossedRiver)
        {
            if (forwardDy == 1 && absDx == 0)
            {
                return MoveValidationResult.Success();
            }
            return MoveValidationResult.Fail(ErrorCodes.INVALID_GEOMETRY, "Tốt chưa qua sông chỉ được tiến thẳng 1 ô.");
        }

        // 4. Sau khi qua sông: Tiến 1 ô (forwardDy == 1 && dx == 0) HOẶC đi ngang 1 ô (forwardDy == 0 && absDx == 1)
        if ((forwardDy == 1 && absDx == 0) || (forwardDy == 0 && absDx == 1))
        {
            return MoveValidationResult.Success();
        }

        return MoveValidationResult.Fail(ErrorCodes.INVALID_GEOMETRY, "Tốt đã qua sông chỉ được tiến 1 ô hoặc đi ngang 1 ô.");
    }
}
