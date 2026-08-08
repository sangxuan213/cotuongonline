namespace UDM18.Client.Models;

using XiangqiOnline.Shared.Contracts;

public static class InitialBoard
{
    public static IReadOnlyList<PieceState> Create()
    {
        var pieces = new List<PieceState>(32);
        AddBackRank(pieces, Side.BLACK, 0);
        AddBackRank(pieces, Side.RED, 9);
        Add(pieces, Side.BLACK, PieceType.CANNON, 1, 2, 1);
        Add(pieces, Side.BLACK, PieceType.CANNON, 7, 2, 2);
        Add(pieces, Side.RED, PieceType.CANNON, 1, 7, 1);
        Add(pieces, Side.RED, PieceType.CANNON, 7, 7, 2);
        for (var i = 0; i < 5; i++)
        {
            Add(pieces, Side.BLACK, PieceType.PAWN, i * 2, 3, i + 1);
            Add(pieces, Side.RED, PieceType.PAWN, i * 2, 6, i + 1);
        }
        return pieces;
    }

    private static void AddBackRank(List<PieceState> pieces, Side side, int y)
    {
        var types = new[]
        {
            PieceType.CHARIOT, PieceType.HORSE, PieceType.ELEPHANT, PieceType.ADVISOR,
            PieceType.GENERAL, PieceType.ADVISOR, PieceType.ELEPHANT, PieceType.HORSE,
            PieceType.CHARIOT
        };
        var counts = new Dictionary<PieceType, int>();
        for (var x = 0; x < types.Length; x++)
        {
            counts.TryGetValue(types[x], out var count);
            counts[types[x]] = ++count;
            Add(pieces, side, types[x], x, y, count);
        }
    }

    private static void Add(List<PieceState> pieces, Side side, PieceType type, int x, int y, int number)
    {
        var pieceId = type == PieceType.GENERAL ? $"{side}_GENERAL" : $"{side}_{type}_{number}";
        pieces.Add(new PieceState(pieceId, side, type, new Coordinate(x, y)));
    }
}
