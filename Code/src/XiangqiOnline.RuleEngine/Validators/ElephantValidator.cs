using XiangqiOnline.RuleEngine.Models;
using XiangqiOnline.Shared.Enums;
using XiangqiOnline.Shared.Models;

namespace XiangqiOnline.RuleEngine.Validators;

/// <summary>
/// Validator kiểm tra luật di chuyển cho Tượng (Elephant).
/// Quy tắc:
/// - Tượng đi đường chéo 2 ô (|dx| = 2, |dy| = 2).
/// - Tượng KHÔNG ĐƯỢC qua sông (BLACK y <= 4, RED y >= 5).
/// - Kiểm tra Mắt Tượng (ô giữa đường chéo 2 ô): Nếu có quân cờ đứng ở mắt Tượng thì bị cản (ELEPHANT_EYE_BLOCKED).
/// - Không được đè lên quân đồng minh.
/// </summary>
public class ElephantValidator : IMoveValidator
{
    public PieceType MatchingPieceType => PieceType.Elephant;

    public MoveValidationResult Validate(BoardState board, PieceState piece, Position to)
    {
        if (!to.IsValid())
        {
            return MoveValidationResult.Fail(ErrorCodes.OUT_OF_BOARD, "Tọa độ đích nằm ngoài bàn cờ.");
        }

        // 1. Tượng không được qua sông
        if (to.HasCrossedRiver(piece.Side))
        {
            return MoveValidationResult.Fail(ErrorCodes.ELEPHANT_CROSSES_RIVER, "Tượng không được đi sang bên kia sông.");
        }

        // 2. Kiểm tra ô đích có quân đồng minh không
        var targetPiece = board.GetPieceAt(to);
        if (targetPiece != null && targetPiece.Side == piece.Side)
        {
            return MoveValidationResult.Fail(ErrorCodes.ALLY_AT_DESTINATION, "Không thể đi vào ô đã có quân đồng minh.");
        }

        // 3. Kiểm tra bước đi (đúng 2 ô chéo)
        int dx = Math.Abs(to.X - piece.Position.X);
        int dy = Math.Abs(to.Y - piece.Position.Y);

        if (dx != 2 || dy != 2)
        {
            return MoveValidationResult.Fail(ErrorCodes.INVALID_GEOMETRY, "Tượng chỉ được đi đường chéo đúng 2 ô.");
        }

        // 4. Kiểm tra Mắt Tượng (ô giữa)
        int eyeX = (piece.Position.X + to.X) / 2;
        int eyeY = (piece.Position.Y + to.Y) / 2;
        var eyePosition = new Position(eyeX, eyeY);

        if (board.GetPieceAt(eyePosition) != null)
        {
            return MoveValidationResult.Fail(ErrorCodes.ELEPHANT_EYE_BLOCKED, "Tượng bị cản mắt Tượng.");
        }

        return MoveValidationResult.Success();
    }
}
