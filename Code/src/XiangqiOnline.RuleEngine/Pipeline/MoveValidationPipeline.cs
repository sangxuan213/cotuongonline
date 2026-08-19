using XiangqiOnline.RuleEngine.Attacks;
using XiangqiOnline.RuleEngine.Checks;
using XiangqiOnline.RuleEngine.Models;
using XiangqiOnline.RuleEngine.Validators;
using XiangqiOnline.Shared.Enums;
using XiangqiOnline.Shared.Models;

namespace XiangqiOnline.RuleEngine.Pipeline;

/// <summary>
/// Pipeline tổng hợp các bước kiểm tra hợp lệ của nước đi cờ Tướng:
/// 1. Kiểm tra tọa độ (OUT_OF_BOARD)
/// 2. Kiểm tra ô đích khác ô nguồn (INVALID_GEOMETRY)
/// 3. Kiểm tra ô nguồn có quân cờ (NO_PIECE_AT_SOURCE)
/// 4. Kiểm tra đúng lượt (NOT_YOUR_TURN) - dựa trên Server Authoritative board.Turn
/// 5. Kiểm tra ô đích không chứa quân đồng minh (ALLY_AT_DESTINATION)
/// 6. Kiểm tra hình học & cản đường riêng từng loại quân (Piece-specific IMoveValidator)
/// 7. Mô phỏng nước đi và kiểm tra tự chiếu (SELF_CHECK / CHECK_NOT_RESOLVED / GENERALS_FACING)
/// </summary>
public class MoveValidationPipeline
{
    private readonly Dictionary<PieceType, IMoveValidator> _validators;
    private readonly SelfCheckValidator _selfCheckValidator;
    private readonly CheckDetector _checkDetector;

    public MoveValidationPipeline(
        SelfCheckValidator? selfCheckValidator = null,
        CheckDetector? checkDetector = null)
    {
        var validatorList = new IMoveValidator[]
        {
            new GeneralValidator(),
            new AdvisorValidator(),
            new ElephantValidator(),
            new HorseValidator(),
            new ChariotValidator(),
            new CannonValidator(),
            new PawnValidator()
        };

        _validators = validatorList.ToDictionary(v => v.MatchingPieceType, v => v);
        _checkDetector = checkDetector ?? CreateDefaultCheckDetector();
        _selfCheckValidator = selfCheckValidator
            ?? new SelfCheckValidator(_checkDetector, new GeneralsFacingDetector());
    }

    /// <summary>
    /// Thực hiện pipeline kiểm tra nước đi. Không ném Exception cho các lỗi nghiệp vụ.
    /// Không tin Side do Client khai trong MoveIntent (dùng server-authoritative board.Turn).
    /// </summary>
    public MoveValidationResult Validate(BoardState board, MoveIntent intent)
    {
        try
        {
            // 1. Kiểm tra tọa độ hợp lệ
            if (!intent.From.IsValid() || !intent.To.IsValid())
            {
                return MoveValidationResult.Fail(ErrorCodes.OUT_OF_BOARD, "Tọa độ nước đi nằm ngoài bàn cờ.");
            }

            // 2. Kiểm tra ô đích khác ô nguồn
            if (intent.From == intent.To)
            {
                return MoveValidationResult.Fail(ErrorCodes.INVALID_GEOMETRY, "Vị trí đích phải khác vị trí nguồn.");
            }

            // 3. Kiểm tra ô nguồn có quân cờ không
            var movingPiece = board.GetPieceAt(intent.From);
            if (movingPiece == null)
            {
                return MoveValidationResult.Fail(ErrorCodes.NO_PIECE_AT_SOURCE, "Không có quân cờ ở vị trí nguồn.");
            }

            // 4. Kiểm tra đúng lượt đi (Server-authoritative check)
            if (movingPiece.Side != board.Turn)
            {
                return MoveValidationResult.Fail(ErrorCodes.NOT_YOUR_TURN, "Chưa tới lượt đi của bạn.");
            }

            // 5. Kiểm tra ô đích không chứa quân đồng minh
            var targetPiece = board.GetPieceAt(intent.To);
            if (targetPiece != null && targetPiece.Side == board.Turn)
            {
                return MoveValidationResult.Fail(ErrorCodes.ALLY_AT_DESTINATION, "Không thể đi vào ô đang chứa quân cùng phe.");
            }
            if (targetPiece?.Type == PieceType.General)
            {
                return MoveValidationResult.Fail(
                    ErrorCodes.INVALID_GEOMETRY,
                    "The General is not captured directly; the game ends at checkmate.");
            }

            // 6. Kiểm tra luật riêng từng loại quân (Hình học & Vật cản)
            if (!_validators.TryGetValue(movingPiece.Type, out var pieceValidator))
            {
                return MoveValidationResult.Fail(ErrorCodes.INVALID_GEOMETRY, "Không tìm thấy luật cho loại quân này.");
            }

            var moveResult = pieceValidator.Validate(board, movingPiece, intent.To);
            if (!moveResult.IsValid)
            {
                return moveResult;
            }

            // 7. Mô phỏng nước đi trên bàn cờ tạm và kiểm tra tự chiếu / tướng đối mặt
            var selfCheckResult = _selfCheckValidator.Validate(board, movingPiece, intent.To);
            if (!selfCheckResult.IsValid)
            {
                return selfCheckResult;
            }

            var temporaryBoard = board.ApplyMove(intent.From, intent.To);
            var opponent = board.Turn == SideColor.Red ? SideColor.Black : SideColor.Red;
            var givesCheck = _checkDetector.Evaluate(temporaryBoard, opponent).IsInCheck;
            return MoveValidationResult.Success(givesCheck);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Bảo vệ Server khỏi crash do lỗi hệ thống không mong muốn
            System.Diagnostics.Trace.TraceError(
                "Move validation failed unexpectedly for {0}->{1}: {2}",
                intent.From, intent.To, ex);
            return MoveValidationResult.Fail(ErrorCodes.INTERNAL_SERVER_ERROR, "Lỗi hệ thống khi kiểm tra nước đi.");
        }
    }

    private static CheckDetector CreateDefaultCheckDetector()
    {
        var facingDetector = new GeneralsFacingDetector();
        var attackDetector = new AttackDetector(new IAttackRule[]
        {
            new GeneralAttackRule(facingDetector),
            new AdvisorAttackRule(),
            new ElephantAttackRule(),
            new HorseAttackRule(),
            new ChariotAttackRule(),
            new CannonAttackRule(),
            new PawnAttackRule()
        });
        return new CheckDetector(attackDetector);
    }
}
