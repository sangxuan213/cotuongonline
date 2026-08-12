using XiangqiOnline.RuleEngine.Models;
using XiangqiOnline.Shared.Enums;
using XiangqiOnline.Shared.Models;

namespace XiangqiOnline.RuleEngine.Validators;

/// <summary>
/// Validator kiểm tra luật di chuyển cho Xe (Chariot).
/// Quy tắc:
/// - Xe đi theo hàng ngang hoặc cột dọc bất kỳ khoảng cách nào.
/// - Đường đi từ vị trí ban đầu đến ô đích phải TRỐNG (không có quân cản đường).
/// - Không được đè lên quân đồng minh.
/// </summary>
public class ChariotValidator : IMoveValidator
{
    public PieceType MatchingPieceType => PieceType.Chariot;

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

        // 2. Xe chỉ đi ngang hoặc dọc
        if (dx != 0 && dy != 0)
        {
            return MoveValidationResult.Fail(ErrorCodes.INVALID_GEOMETRY, "Xe chỉ được di chuyển theo hàng ngang hoặc cột dọc.");
        }

        if (dx == 0 && dy == 0)
        {
            return MoveValidationResult.Fail(ErrorCodes.INVALID_GEOMETRY, "Nước đi phải thay đổi vị trí.");
        }

        // 3. Kiểm tra vật cản trên đường đi (Path blocking)
        int stepX = Math.Sign(dx);
        int stepY = Math.Sign(dy);

        int currX = piece.Position.X + stepX;
        int currY = piece.Position.Y + stepY;

        while (currX != to.X || currY != to.Y)
        {
            var posBetween = new Position(currX, currY);
            if (board.GetPieceAt(posBetween) != null)
            {
                return MoveValidationResult.Fail(ErrorCodes.PATH_BLOCKED, "Xe bị cản đường.");
            }

            currX += stepX;
            currY += stepY;
        }

        return MoveValidationResult.Success();
    }
}
