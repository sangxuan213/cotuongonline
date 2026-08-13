using XiangqiOnline.RuleEngine.Models;
using XiangqiOnline.RuleEngine.Validators;
using XiangqiOnline.Shared.Enums;
using XiangqiOnline.Shared.Models;

namespace XiangqiOnline.RuleEngine.Attacks;

public sealed class GeneralAttackRule : IAttackRule
{
    private readonly GeneralValidator _validator = new();
    private readonly GeneralsFacingDetector _generalsFacingDetector;

    public GeneralAttackRule(GeneralsFacingDetector generalsFacingDetector)
    {
        _generalsFacingDetector = generalsFacingDetector
            ?? throw new ArgumentNullException(nameof(generalsFacingDetector));
    }

    public PieceType MatchingPieceType => PieceType.General;

    public bool CanAttack(BoardState board, PieceState attacker, Position target)
    {
        if (_validator.Validate(board, attacker, target).IsValid)
        {
            return true;
        }

        var targetPiece = board.GetPieceAt(target);
        return attacker.Type == PieceType.General
            && targetPiece is { Type: PieceType.General }
            && targetPiece.Side != attacker.Side
            && _generalsFacingDetector.AreGeneralsFacing(board);
    }
}
