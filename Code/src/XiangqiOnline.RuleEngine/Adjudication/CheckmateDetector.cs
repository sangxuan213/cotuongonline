using XiangqiOnline.RuleEngine.Checks;
using XiangqiOnline.RuleEngine.LegalMoves;
using XiangqiOnline.RuleEngine.Models;
using XiangqiOnline.Shared.Enums;

namespace XiangqiOnline.RuleEngine.Adjudication;

public sealed class CheckmateDetector
{
    private readonly CheckDetector _checkDetector;
    private readonly LegalMoveGenerator _legalMoveGenerator;

    public CheckmateDetector(CheckDetector checkDetector, LegalMoveGenerator legalMoveGenerator)
    {
        _checkDetector = checkDetector ?? throw new ArgumentNullException(nameof(checkDetector));
        _legalMoveGenerator = legalMoveGenerator ?? throw new ArgumentNullException(nameof(legalMoveGenerator));
    }

    public bool IsCheckmate(BoardState board, SideColor side)
    {
        ArgumentNullException.ThrowIfNull(board);
        return _checkDetector.Evaluate(board, side).IsInCheck
            && !_legalMoveGenerator.HasAny(board, side);
    }
}
