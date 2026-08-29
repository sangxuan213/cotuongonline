using XiangqiOnline.RuleEngine.Models;
using XiangqiOnline.Shared.Enums;

namespace XiangqiOnline.RuleEngine.Adjudication;

public sealed record TerminationFacts(
    bool IsTerminal,
    SideColor? Winner,
    string? EndReason,
    bool IsCheck,
    int LegalMoveCount);

public sealed class GameTerminationDetector
{
    private readonly LegalMoveGenerator _moves;

    public GameTerminationDetector(LegalMoveGenerator? moves = null) =>
        _moves = moves ?? new LegalMoveGenerator();

    public TerminationFacts Evaluate(BoardState board)
    {
        ArgumentNullException.ThrowIfNull(board);
        var side = board.Turn;
        var legal = _moves.Generate(board, side);
        var isCheck = RuleEngineServices.CreateCheckDetector().Evaluate(board, side).IsInCheck;
        if (legal.Count > 0)
            return new TerminationFacts(false, null, null, isCheck, legal.Count);

        var winner = side == SideColor.Red ? SideColor.Black : SideColor.Red;
        return new TerminationFacts(
            true,
            winner,
            isCheck ? "CHECKMATE" : "STALEMATE_NO_LEGAL_MOVE",
            isCheck,
            0);
    }
}
