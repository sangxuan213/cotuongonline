using XiangqiOnline.RuleEngine.Models;
using XiangqiOnline.Shared.Enums;
using XiangqiOnline.Shared.Models;

namespace XiangqiOnline.RuleEngine.Validators;

/// <summary>
/// Validator kiểm tra luật di chuyển cho Pháo (Cannon).
/// Quy tắc:
/// - Pháo đi theo hàng ngang hoặc cột dọc.
/// - Khi DI CHUYỂN KHÔNG ĂN QUÂN (ô đích trống): Đường đi phải hoàn toàn TRỐNG (0 ngòi).
/// - Khi ĂN QUÂN (ô đích có quân địch): Trên đường đi phải có ĐÚNG 1 NGÒI (1 quân cờ làm giá đỡ).
/// - Không được ăn quân đồng minh.
/// </summary>
public class CannonValidator : IMoveValidator
{
    public PieceType MatchingPieceType => PieceType.Cannon;

    public MoveValidationResult Validate(BoardState board, PieceState piece, Position to)
    {
        if (!to.IsValid())
        {
            return MoveValidationResult.Fail(ErrorCodes.OUT_OF_BOARD, "Tọa độ đích nằm ngoài bàn cờ.");
        }

        int dx = to.X - piece.Position.X;
        int dy = to.Y - piece.Position.Y;

        // 1. Pháo chỉ đi ngang hoặc dọc
        if (dx != 0 && dy != 0)
        {
            return MoveValidationResult.Fail(ErrorCodes.INVALID_GEOMETRY, "Pháo chỉ được di chuyển theo hàng ngang hoặc cột dọc.");
        }

        if (dx == 0 && dy == 0)
        {
            return MoveValidationResult.Fail(ErrorCodes.INVALID_GEOMETRY, "Nước đi phải thay đổi vị trí.");
        }

        // 2. Đếm số lượng ngòi (quân cờ đứng giữa ô nguồn và ô đích)
        int stepX = Math.Sign(dx);
        int stepY = Math.Sign(dy);

        int currX = piece.Position.X + stepX;
        int currY = piece.Position.Y + stepY;
        int mountCount = 0;

        while (currX != to.X || currY != to.Y)
        {
            var posBetween = new Position(currX, currY);
            if (board.GetPieceAt(posBetween) != null)
            {
                mountCount++;
            }

            currX += stepX;
            currY += stepY;
        }

        var targetPiece = board.GetPieceAt(to);

        // 3. Trường hợp 1: Di chuyển không ăn quân (ô đích trống)
        if (targetPiece == null)
        {
            if (mountCount > 0)
            {
                return MoveValidationResult.Fail(ErrorCodes.PATH_BLOCKED, "Pháo di chuyển không ăn quân thì đường đi phải trống (0 ngòi).");
            }
            return MoveValidationResult.Success();
        }

        // 4. Trường hợp 2: Ăn quân (ô đích có quân cờ)
        if (targetPiece.Side == piece.Side)
        {
            return MoveValidationResult.Fail(ErrorCodes.ALLY_AT_DESTINATION, "Không thể ăn quân đồng minh.");
        }

        if (mountCount != 1)
        {
            return MoveValidationResult.Fail(ErrorCodes.CANNON_SCREEN_INVALID, "Pháo ăn quân phải có đúng 1 ngòi.");
        }

        return MoveValidationResult.Success();
    }
}
