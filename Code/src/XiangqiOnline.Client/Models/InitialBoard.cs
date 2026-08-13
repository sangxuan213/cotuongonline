namespace UDM18.Client.Models;

using XiangqiOnline.Shared.Enums;
using XiangqiOnline.Shared.Models;

public static class InitialBoard
{
    public static IReadOnlyList<PieceState> Create()
    {
        var pieces = new List<PieceState>(32);
        AddBackRank(pieces, SideColor.Black, 0);
        AddBackRank(pieces, SideColor.Red, 9);
        Add(pieces, SideColor.Black, PieceType.Cannon, 1, 2, 1);
        Add(pieces, SideColor.Black, PieceType.Cannon, 7, 2, 2);
        Add(pieces, SideColor.Red, PieceType.Cannon, 1, 7, 1);
        Add(pieces, SideColor.Red, PieceType.Cannon, 7, 7, 2);
        for (var i = 0; i < 5; i++)
        {
            Add(pieces, SideColor.Black, PieceType.Pawn, i * 2, 3, i + 1);
            Add(pieces, SideColor.Red, PieceType.Pawn, i * 2, 6, i + 1);
        }
        return pieces;
    }

    private static void AddBackRank(List<PieceState> pieces, SideColor side, int y)
    {
        var types = new[]
        {
            PieceType.Chariot, PieceType.Horse, PieceType.Elephant, PieceType.Advisor,
            PieceType.General, PieceType.Advisor, PieceType.Elephant, PieceType.Horse,
            PieceType.Chariot
        };
        var counts = new Dictionary<PieceType, int>();
        for (var x = 0; x < types.Length; x++)
        {
            counts.TryGetValue(types[x], out var count);
            counts[types[x]] = ++count;
            Add(pieces, side, types[x], x, y, count);
        }
    }

    private static void Add(List<PieceState> pieces, SideColor side, PieceType type, int x, int y, int number)
    {
        var sideName = side.ToString().ToUpperInvariant();
        var typeName = type.ToString().ToUpperInvariant();
        var pieceId = type == PieceType.General ? $"{sideName}_GENERAL" : $"{sideName}_{typeName}_{number}";
        pieces.Add(new PieceState(pieceId, side, type, new Position(x, y)));
    }
}
