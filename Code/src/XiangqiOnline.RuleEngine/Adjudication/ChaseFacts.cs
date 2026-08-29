using XiangqiOnline.RuleEngine.Models;
using XiangqiOnline.Shared.Enums;

namespace XiangqiOnline.RuleEngine.Adjudication;

public sealed class ProtectedPieceEvaluator
{
    public bool IsProtected(BoardState board, PieceState victim)
    {
        ArgumentNullException.ThrowIfNull(board);
        ArgumentNullException.ThrowIfNull(victim);
        var boardWithoutVictim = board with { Pieces = board.Pieces.Remove(victim.Position) };
        return RuleEngineServices.CreateAttackDetector()
            .FindAttackers(boardWithoutVictim, victim.Position, victim.Side)
            .Any(piece => piece.Id != victim.Id);
    }
}

public sealed class ChaseVictimDetector
{
    private readonly ProtectedPieceEvaluator _protection = new();

    public IReadOnlyList<PieceState> FindVictims(MoveApplicationResult move)
    {
        var moved = move.After.GetActivePieces(move.MovingPiece.Side)
            .Single(piece => piece.Id == move.MovingPiece.Id);
        if (moved.Type == PieceType.General) return Array.Empty<PieceState>();
        if (moved.Type == PieceType.Pawn && !HasCrossedRiver(moved)) return Array.Empty<PieceState>();

        var detector = RuleEngineServices.CreateAttackDetector();
        var victims = new List<PieceState>();
        foreach (var candidate in move.After.GetActivePieces(Opponent(moved.Side)))
        {
            if (candidate.Type == PieceType.General) continue;
            var attackers = detector.FindAttackers(move.After, candidate.Position, moved.Side);
            if (attackers.Any(attacker => attacker.Id == moved.Id) && !_protection.IsProtected(move.After, candidate))
                victims.Add(candidate);
        }
        return victims.OrderBy(piece => piece.Id, StringComparer.Ordinal).ToArray();
    }

    private static bool HasCrossedRiver(PieceState pawn) =>
        pawn.Side == SideColor.Red ? pawn.Position.Y <= 4 : pawn.Position.Y >= 5;

    private static SideColor Opponent(SideColor side) =>
        side == SideColor.Red ? SideColor.Black : SideColor.Red;
}
