using XiangqiOnline.RuleEngine.Attacks;
using XiangqiOnline.RuleEngine.Models;
using XiangqiOnline.Shared.Models;

namespace XiangqiOnline.RuleEngine.Checks;

public sealed class SelfCheckValidator
{
    private readonly CheckDetector _checkDetector;
    private readonly GeneralsFacingDetector _generalsFacingDetector;

    public SelfCheckValidator(
        CheckDetector checkDetector,
        GeneralsFacingDetector generalsFacingDetector)
    {
        _checkDetector = checkDetector ?? throw new ArgumentNullException(nameof(checkDetector));
        _generalsFacingDetector = generalsFacingDetector
            ?? throw new ArgumentNullException(nameof(generalsFacingDetector));
    }

    public MoveValidationResult Validate(
        BoardState board,
        PieceState movingPiece,
        Position target)
    {
        ArgumentNullException.ThrowIfNull(board);
        ArgumentNullException.ThrowIfNull(movingPiece);

        if (!target.IsValid())
        {
            throw new ArgumentOutOfRangeException(nameof(target), target, "Target must be on the canonical board.");
        }

        var activeSourcePiece = board.GetPieceAt(movingPiece.Position);
        if (activeSourcePiece is null
            || activeSourcePiece.Id != movingPiece.Id
            || activeSourcePiece.Side != movingPiece.Side
            || activeSourcePiece.Type != movingPiece.Type
            || !movingPiece.IsAlive)
        {
            throw new InvalidOperationException(
                $"Supplied moving piece '{movingPiece.Id}' does not match the active canonical piece at source {movingPiece.Position}.");
        }

        var before = _checkDetector.Evaluate(board, movingPiece.Side);
        var temporaryBoard = board.ApplyMove(movingPiece.Position, target);

        if (_generalsFacingDetector.AreGeneralsFacing(temporaryBoard))
        {
            return MoveValidationResult.Fail(
                ErrorCodes.GENERALS_FACING,
                "The move leaves the two Generals facing each other on an open file.");
        }

        var after = _checkDetector.Evaluate(temporaryBoard, movingPiece.Side);
        if (!after.IsInCheck)
        {
            return MoveValidationResult.Success();
        }

        return before.IsInCheck
            ? MoveValidationResult.Fail(
                ErrorCodes.CHECK_NOT_RESOLVED,
                "The move does not resolve the existing check.")
            : MoveValidationResult.Fail(
                ErrorCodes.SELF_CHECK,
                "The move exposes the moving side's General to attack.");
    }
}
