using XiangqiOnline.RuleEngine.Models;
using XiangqiOnline.Shared.Enums;
using XiangqiOnline.Shared.Models;

namespace XiangqiOnline.RuleEngine.Validators;

/// <summary>
/// Interface kiểm tra tính hợp lệ về hình học và vật cản của một loại quân cờ.
/// </summary>
public interface IMoveValidator
{
    PieceType MatchingPieceType { get; }
    MoveValidationResult Validate(BoardState board, PieceState piece, Position to);
}
