using XiangqiOnline.RuleEngine.Models;
using XiangqiOnline.RuleEngine.Validators;
using XiangqiOnline.Shared.Enums;
using XiangqiOnline.Shared.Models;

namespace XiangqiOnline.RuleEngine.Pipeline;

/// <summary>
/// Pipeline tổng hợp 7 bước kiểm tra hợp lệ của nước đi cờ Tướng:
/// Bước 1: Kiểm tra lượt đi (NOT_YOUR_TURN)
/// Bước 2: Kiểm tra tính hợp lệ của tọa độ Nguồn & Đích (INVALID_COORDINATE)
/// Bước 3: Kiểm tra vị trí Đích khác Nguồn (SAME_DESTINATION)
/// Bước 4: Kiểm tra ô Nguồn có quân cờ không (NO_PIECE_AT_SOURCE)
/// Bước 5: Kiểm tra quân cờ thuộc phe của người chơi (NOT_YOUR_PIECE)
/// Bước 6: Kiểm tra ô Đích không chứa quân đồng minh (DESTINATION_OCCUPIED_BY_FRIEND)
/// Bước 7: Kiểm tra hình học & vật cản riêng theo từng loại quân (Piece-specific IMoveValidator)
/// </summary>
public class MoveValidationPipeline
{
    private readonly Dictionary<PieceType, IMoveValidator> _validators;

    public MoveValidationPipeline()
    {
        var validatorList = new IMoveValidator[]
        {
            new GeneralValidator(),
            new AdvisorValidator(),
            new ElephantValidator(),
            new HorseValidator(),
            new RookValidator(),
            new CannonValidator(),
            new PawnValidator()
        };

        _validators = validatorList.ToDictionary(v => v.MatchingPieceType, v => v);
    }

    /// <summary>
    /// Thực hiện pipeline kiểm tra 7 bước. Không ném Exception cho các lỗi nghiệp vụ.
    /// </summary>
    public MoveValidationResult Validate(BoardState board, MoveIntent intent)
    {
        try
        {
            // Bước 1: Kiểm tra đúng lượt đi không
            if (intent.Side != board.Turn)
            {
                return MoveValidationResult.Fail(ErrorCodes.NOT_YOUR_TURN, "Chưa tới lượt đi của bạn.");
            }

            // Bước 2: Kiểm tra tọa độ hợp lệ
            if (!intent.From.IsValid() || !intent.To.IsValid())
            {
                return MoveValidationResult.Fail(ErrorCodes.INVALID_COORDINATE, "Tọa độ nước đi không hợp lệ.");
            }

            // Bước 3: Kiểm tra ô đích khác ô nguồn
            if (intent.From == intent.To)
            {
                return MoveValidationResult.Fail(ErrorCodes.SAME_DESTINATION, "Vị trí đích phải khác vị trí nguồn.");
            }

            // Bước 4: Kiểm tra ô nguồn có quân cờ không
            var movingPiece = board.GetPieceAt(intent.From);
            if (movingPiece == null)
            {
                return MoveValidationResult.Fail(ErrorCodes.NO_PIECE_AT_SOURCE, "Không có quân cờ ở vị trí nguồn.");
            }

            // Bước 5: Kiểm tra quân cờ có đúng của người chơi không
            if (movingPiece.Side != intent.Side)
            {
                return MoveValidationResult.Fail(ErrorCodes.NOT_YOUR_PIECE, "Bạn không thể di chuyển quân của đối phương.");
            }

            // Bước 6: Kiểm tra ô đích không có quân đồng minh
            var targetPiece = board.GetPieceAt(intent.To);
            if (targetPiece != null && targetPiece.Side == intent.Side)
            {
                return MoveValidationResult.Fail(ErrorCodes.DESTINATION_OCCUPIED_BY_FRIEND, "Không thể đi vào ô đang chứa quân cùng phe.");
            }

            // Bước 7: Kiểm tra luật riêng từng loại quân (Hình học & Vật cản)
            if (!_validators.TryGetValue(movingPiece.Type, out var pieceValidator))
            {
                return MoveValidationResult.Fail(ErrorCodes.ILLEGAL_PIECE_MOVE, "Không tìm thấy luật cho loại quân này.");
            }

            return pieceValidator.Validate(board, movingPiece, intent.To);
        }
        catch (Exception ex)
        {
            // Bảo vệ Server khỏi crash do lỗi không mong muốn
            return MoveValidationResult.Fail("ERR_INTERNAL_ERROR", $"Lỗi hệ thống khi kiểm tra nước đi: {ex.Message}");
        }
    }
}
