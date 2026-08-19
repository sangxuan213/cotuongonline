using XiangqiOnline.RuleEngine.LegalMoves;
using XiangqiOnline.RuleEngine.Models;
using XiangqiOnline.Shared.Enums;

namespace XiangqiOnline.RuleEngine.Adjudication;

public sealed class NoLegalMoveDetector
{
    private readonly LegalMoveGenerator _legalMoveGenerator;

    public NoLegalMoveDetector(LegalMoveGenerator legalMoveGenerator)
    {
        _legalMoveGenerator = legalMoveGenerator
            ?? throw new ArgumentNullException(nameof(legalMoveGenerator));
    }

    public bool HasNoLegalMove(BoardState board, SideColor side)
    {
        ArgumentNullException.ThrowIfNull(board);
        return !_legalMoveGenerator.HasAny(board, side);
    }
}
