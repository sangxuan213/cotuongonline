using XiangqiOnline.RuleEngine.Models;
using XiangqiOnline.Shared.Enums;
using XiangqiOnline.Shared.Models;

namespace XiangqiOnline.RuleEngine.Validators;

/// <summary>
/// Validator kiểm tra luật di chuyển cho Tướng (General).
/// Quy tắc:
/// - Tướng chỉ được di chuyển trong Cung 3x3.
/// - Mỗi lần đi đúng 1 ô theo chiều ngang hoặc chiều dọc.
/// - Không được đè lên quân đồng minh.
/// </summary>
public class GeneralValidator : IMoveValidator
{
    public PieceType MatchingPieceType => PieceType.General;

    public MoveValidationResult Validate(BoardState board, PieceState piece, Position to)
    {
        if (!to.IsValid())
        {
            return MoveValidationResult.Fail(ErrorCodes.INVALID_COORDINATE, "Tọa độ đích nằm ngoài bàn cờ.");
        }

        // 1. Phải ở trong Cung
        if (!to.IsInPalace(piece.Side))
        {
            return MoveValidationResult.Fail(ErrorCodes.OUT_OF_PALACE, "Tướng không được đi ra khỏi Cung.");
        }

        // 2. Kiểm tra ô đích có quân đồng minh không
        var targetPiece = board.GetPieceAt(to);
        if (targetPiece != null && targetPiece.Side == piece.Side)
        {
            return MoveValidationResult.Fail(ErrorCodes.DESTINATION_OCCUPIED_BY_FRIEND, "Không thể đi vào ô đã có quân đồng minh.");
        }

        // 3. Kiểm tra bước đi (đúng 1 ô ngang hoặc 1 ô dọc)
        int dx = Math.Abs(to.X - piece.Position.X);
        int dy = Math.Abs(to.Y - piece.Position.Y);

        if ((dx == 1 && dy == 0) || (dx == 0 && dy == 1))
        {
            return MoveValidationResult.Success();
        }

        return MoveValidationResult.Fail(ErrorCodes.ILLEGAL_PIECE_MOVE, "Tướng chỉ được di chuyển 1 ô theo chiều ngang hoặc dọc.");
    }
}
