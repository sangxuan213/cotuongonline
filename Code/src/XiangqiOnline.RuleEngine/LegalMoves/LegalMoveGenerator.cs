using XiangqiOnline.RuleEngine.Models;
using XiangqiOnline.RuleEngine.Pipeline;
using XiangqiOnline.Shared.Enums;
using XiangqiOnline.Shared.Models;

namespace XiangqiOnline.RuleEngine.LegalMoves;

public sealed class LegalMoveGenerator
{
    private static readonly (int X, int Y)[] OrthogonalSteps =
        [(-1, 0), (0, -1), (0, 1), (1, 0)];

    private static readonly (int X, int Y)[] DiagonalSteps =
        [(-1, -1), (-1, 1), (1, -1), (1, 1)];

    private static readonly (int X, int Y)[] HorseSteps =
        [(-2, -1), (-2, 1), (-1, -2), (-1, 2), (1, -2), (1, 2), (2, -1), (2, 1)];

    private readonly MoveValidationPipeline _pipeline;

    public LegalMoveGenerator(MoveValidationPipeline? pipeline = null)
    {
        _pipeline = pipeline ?? new MoveValidationPipeline();
    }

    public IReadOnlyList<LegalMove> Generate(BoardState board, SideColor side)
    {
        ArgumentNullException.ThrowIfNull(board);

        var boardForSide = board.Turn == side ? board : board with { Turn = side };
        var legalMoves = new List<LegalMove>();

        foreach (var piece in boardForSide.GetActivePieces(side)
                     .OrderBy(piece => piece.Id, StringComparer.Ordinal))
        {
            foreach (var target in GenerateCandidates(piece)
                         .Where(target => target.IsValid())
                         .Distinct()
                         .OrderBy(target => target.Y)
                         .ThenBy(target => target.X))
            {
                var intent = new MoveIntent("legal-move-probe", piece.Position, target, 0);
                if (_pipeline.Validate(boardForSide, intent).IsValid)
                {
                    legalMoves.Add(new LegalMove(piece.Id, piece.Position, target));
                }
            }
        }

        return legalMoves;
    }

    public bool HasAny(BoardState board, SideColor side)
    {
        ArgumentNullException.ThrowIfNull(board);

        var boardForSide = board.Turn == side ? board : board with { Turn = side };
        foreach (var piece in boardForSide.GetActivePieces(side)
                     .OrderBy(piece => piece.Id, StringComparer.Ordinal))
        {
            foreach (var target in GenerateCandidates(piece)
                         .Where(target => target.IsValid())
                         .Distinct()
                         .OrderBy(target => target.Y)
                         .ThenBy(target => target.X))
            {
                var intent = new MoveIntent("legal-move-probe", piece.Position, target, 0);
                if (_pipeline.Validate(boardForSide, intent).IsValid)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static IEnumerable<Position> GenerateCandidates(PieceState piece)
    {
        return piece.Type switch
        {
            PieceType.General => Offset(piece.Position, OrthogonalSteps, 1),
            PieceType.Advisor => Offset(piece.Position, DiagonalSteps, 1),
            PieceType.Elephant => Offset(piece.Position, DiagonalSteps, 2),
            PieceType.Horse => Offset(piece.Position, HorseSteps, 1),
            PieceType.Chariot or PieceType.Cannon => OrthogonalRays(piece.Position),
            PieceType.Pawn => PawnTargets(piece),
            _ => []
        };
    }

    private static IEnumerable<Position> Offset(
        Position origin,
        IEnumerable<(int X, int Y)> offsets,
        int scale)
    {
        return offsets.Select(offset =>
            new Position(origin.X + offset.X * scale, origin.Y + offset.Y * scale));
    }

    private static IEnumerable<Position> OrthogonalRays(Position origin)
    {
        for (var x = 0; x <= 8; x++)
        {
            if (x != origin.X)
                yield return new Position(x, origin.Y);
        }

        for (var y = 0; y <= 9; y++)
        {
            if (y != origin.Y)
                yield return new Position(origin.X, y);
        }
    }

    private static IEnumerable<Position> PawnTargets(PieceState pawn)
    {
        var forward = pawn.Side == SideColor.Red ? -1 : 1;
        yield return new Position(pawn.Position.X, pawn.Position.Y + forward);

        if (pawn.Position.HasCrossedRiver(pawn.Side))
        {
            yield return new Position(pawn.Position.X - 1, pawn.Position.Y);
            yield return new Position(pawn.Position.X + 1, pawn.Position.Y);
        }
    }
}
