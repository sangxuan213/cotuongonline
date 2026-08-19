using XiangqiOnline.RuleEngine.Checks;
using XiangqiOnline.RuleEngine.Attacks;
using XiangqiOnline.RuleEngine.LegalMoves;
using XiangqiOnline.RuleEngine.Models;
using XiangqiOnline.Shared.Enums;

namespace XiangqiOnline.RuleEngine.Adjudication;

public sealed class GameResultResolver
{
    private readonly CheckDetector _checkDetector;
    private readonly CheckmateDetector _checkmateDetector;
    private readonly NoLegalMoveDetector _noLegalMoveDetector;

    public GameResultResolver(
        CheckDetector checkDetector,
        LegalMoveGenerator legalMoveGenerator)
    {
        _checkDetector = checkDetector ?? throw new ArgumentNullException(nameof(checkDetector));
        _checkmateDetector = new CheckmateDetector(checkDetector, legalMoveGenerator);
        _noLegalMoveDetector = new NoLegalMoveDetector(legalMoveGenerator);
    }

    public static GameResultResolver CreateDefault()
    {
        var facingDetector = new GeneralsFacingDetector();
        var checkDetector = new CheckDetector(new AttackDetector(new IAttackRule[]
        {
            new GeneralAttackRule(facingDetector),
            new AdvisorAttackRule(),
            new ElephantAttackRule(),
            new HorseAttackRule(),
            new ChariotAttackRule(),
            new CannonAttackRule(),
            new PawnAttackRule()
        }));
        var pipeline = new Pipeline.MoveValidationPipeline(
            new SelfCheckValidator(checkDetector, facingDetector),
            checkDetector);
        return new GameResultResolver(checkDetector, new LegalMoveGenerator(pipeline));
    }

    public GameResult? ResolveBoard(BoardState board)
    {
        ArgumentNullException.ThrowIfNull(board);

        var sideWithoutMove = board.Turn;
        if (!_noLegalMoveDetector.HasNoLegalMove(board, sideWithoutMove))
            return null;

        var winner = OpponentOf(sideWithoutMove);
        if (_checkmateDetector.IsCheckmate(board, sideWithoutMove))
        {
            return Win(winner, GameEndReason.Checkmate, $"{sideWithoutMove} is checkmated.");
        }

        var isInCheck = _checkDetector.Evaluate(board, sideWithoutMove).IsInCheck;
        if (isInCheck)
            throw new InvalidOperationException("Checkmate state was not classified consistently.");

        return Win(winner, GameEndReason.NoLegalMove, $"{sideWithoutMove} has no legal move.");
    }

    public GameResult ResolveTimeout(SideColor timedOutSide) =>
        Win(OpponentOf(timedOutSide), GameEndReason.Timeout, $"{timedOutSide} ran out of time.");

    public GameResult ResolveResignation(SideColor resigningSide) =>
        Win(OpponentOf(resigningSide), GameEndReason.Resignation, $"{resigningSide} resigned.");

    public GameResult ResolveDrawAgreement() =>
        new("DRAW", GameEndReason.DrawAgreement, null, "Both players agreed to a draw.");

    public GameResult ResolveByPriority(IEnumerable<GameResult> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        return candidates
            .OrderByDescending(candidate => PriorityOf(candidate.EndReason))
            .ThenBy(candidate => candidate.ResultType, StringComparer.Ordinal)
            .FirstOrDefault()
            ?? throw new ArgumentException("At least one terminal candidate is required.", nameof(candidates));
    }

    public static int PriorityOf(GameEndReason reason) => reason switch
    {
        GameEndReason.Checkmate => 500,
        GameEndReason.NoLegalMove => 500,
        GameEndReason.Resignation => 400,
        GameEndReason.Timeout => 300,
        GameEndReason.DrawAgreement => 100,
        _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, null)
    };

    private static GameResult Win(SideColor winner, GameEndReason reason, string explanation) =>
        new(winner == SideColor.Red ? "RED_WIN" : "BLACK_WIN", reason, winner, explanation);

    private static SideColor OpponentOf(SideColor side) =>
        side == SideColor.Red ? SideColor.Black : SideColor.Red;
}
