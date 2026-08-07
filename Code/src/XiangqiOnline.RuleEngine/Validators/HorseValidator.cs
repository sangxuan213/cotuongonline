using XiangqiOnline.RuleEngine.Models;
using XiangqiOnline.Shared.Enums;
using XiangqiOnline.Shared.Models;

namespace XiangqiOnline.RuleEngine.Validators;

/// <summary>
/// Validator kiểm tra luật di chuyển cho Mã (Horse).
/// Quy tắc:
/// - Mã đi theo hình chữ Nhật: (dx=1, dy=2) hoặc (dx=2, dy=1).
/// - Kiểm tra Chân Mã (Horse Foot):
///   + Nếu nhảy dọc (dx=1, dy=2): Chân Mã là ô (from.X, from.Y + sign(dy)).
///   + Nếu nhảy ngang (dx=2, dy=1): Chân Mã là ô (from.X + sign(dx), from.Y).
///   Nếu có quân cờ đứng ở Chân Mã -> Bị cản chân Mã (Horse Foot Blocked).
/// - Không được đè lên quân đồng minh.
/// </summary>
public class HorseValidator : IMoveValidator
{
    public PieceType MatchingPieceType => PieceType.Horse;

    public MoveValidationResult Validate(BoardState board, PieceState piece, Position to)
    {
        if (!to.IsValid())
        {
            return MoveValidationResult.Fail(ErrorCodes.INVALID_COORDINATE, "Tọa độ đích nằm ngoài bàn cờ.");
        }

        // 1. Kiểm tra ô đích có quân đồng minh không
        var targetPiece = board.GetPieceAt(to);
        if (targetPiece != null && targetPiece.Side == piece.Side)
        {
            return MoveValidationResult.Fail(ErrorCodes.DESTINATION_OCCUPIED_BY_FRIEND, "Không thể đi vào ô đã có quân đồng minh.");
        }

        int dx = to.X - piece.Position.X;
        int dy = to.Y - piece.Position.Y;
        int absDx = Math.Abs(dx);
        int absDy = Math.Abs(dy);

        // 2. Kiểm tra bước đi hình chữ Nhật
        Position footPos;
        if (absDx == 1 && absDy == 2)
        {
            // Nhảy dọc: Chân Mã nằm cùng cột X, nhích 1 ô theo hướng Y
            footPos = new Position(piece.Position.X, piece.Position.Y + Math.Sign(dy));
        }
        else if (absDx == 2 && absDy == 1)
        {
            // Nhảy ngang: Chân Mã nằm cùng hàng Y, nhích 1 ô theo hướng X
            footPos = new Position(piece.Position.X + Math.Sign(dx), piece.Position.Y);
        }
        else
        {
            return MoveValidationResult.Fail(ErrorCodes.ILLEGAL_PIECE_MOVE, "Mã chỉ được di chuyển theo hình chữ Nhật (1x2 hoặc 2x1).");
        }

        // 3. Kiểm tra cản chân Mã
        if (board.GetPieceAt(footPos) != null)
        {
            return MoveValidationResult.Fail(ErrorCodes.HORSE_FOOT_BLOCKED, "Mã bị cản chân Mã.");
        }

        return MoveValidationResult.Success();
    }
}
