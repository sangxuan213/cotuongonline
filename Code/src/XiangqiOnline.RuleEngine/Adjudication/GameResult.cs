using XiangqiOnline.Shared.Enums;

namespace XiangqiOnline.RuleEngine.Adjudication;

public enum GameEndReason
{
    Checkmate,
    NoLegalMove,
    Timeout,
    Resignation,
    DrawAgreement
}

public sealed record GameResult(
    string ResultType,
    GameEndReason EndReason,
    SideColor? WinnerSide,
    string Explanation)
{
    public string EndReasonCode => EndReason switch
    {
        GameEndReason.Checkmate => "CHECKMATE",
        GameEndReason.NoLegalMove => "NO_LEGAL_MOVE",
        GameEndReason.Timeout => "TIMEOUT",
        GameEndReason.Resignation => "RESIGNATION",
        GameEndReason.DrawAgreement => "DRAW_AGREEMENT",
        _ => throw new ArgumentOutOfRangeException()
    };
}
