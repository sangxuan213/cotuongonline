using XiangqiOnline.RuleEngine.Models;
using XiangqiOnline.Shared.Enums;
using XiangqiOnline.Shared.Models;

namespace XiangqiOnline.RuleEngine.Validators;

/// <summary>
/// Validator kiểm tra luật di chuyển cho Sĩ (Advisor).
/// Quy tắc:
/// - Sĩ chỉ được di chuyển trong Cung 3x3.
/// - Mỗi lần đi đúng 1 ô theo đường chéo (|dx| = 1, |dy| = 1).
/// - Không được đè lên quân đồng minh.
/// </summary>
public class AdvisorValidator : IMoveValidator
{
    public PieceType MatchingPieceType => PieceType.Advisor;

    public MoveValidationResult Validate(BoardState board, PieceState piece, Position to)
    {
        if (!to.IsValid())
        {
            return MoveValidationResult.Fail(ErrorCodes.INVALID_COORDINATE, "Tọa độ đích nằm ngoài bàn cờ.");
        }

        // 1. Phải ở trong Cung
        if (!to.IsInPalace(piece.Side))
        {
            return MoveValidationResult.Fail(ErrorCodes.OUT_OF_PALACE, "Sĩ không được đi ra khỏi Cung.");
        }

        // 2. Kiểm tra ô đích có quân đồng minh không
        var targetPiece = board.GetPieceAt(to);
        if (targetPiece != null && targetPiece.Side == piece.Side)
        {
            return MoveValidationResult.Fail(ErrorCodes.DESTINATION_OCCUPIED_BY_FRIEND, "Không thể đi vào ô đã có quân đồng minh.");
        }

        // 3. Kiểm tra đường đi (đúng 1 ô chéo)
        int dx = Math.Abs(to.X - piece.Position.X);
        int dy = Math.Abs(to.Y - piece.Position.Y);

        if (dx == 1 && dy == 1)
        {
            return MoveValidationResult.Success();
        }

        return MoveValidationResult.Fail(ErrorCodes.ILLEGAL_PIECE_MOVE, "Sĩ chỉ được di chuyển 1 ô theo đường chéo.");
    }
}
