using System.Collections.Immutable;
using XiangqiOnline.Shared.Enums;
using XiangqiOnline.Shared.Models;

namespace XiangqiOnline.RuleEngine.Models;

/// <summary>
/// Trạng thái bất biến (Immutable) của bàn cờ Tướng 9x10 (90 giao điểm).
/// BLACK ở trên (y=0..4), RED ở dưới (y=5..9).
/// </summary>
public record BoardState
{
    public ImmutableDictionary<Position, PieceState> Pieces { get; init; }
    public SideColor Turn { get; init; }

    public BoardState(ImmutableDictionary<Position, PieceState> pieces, SideColor turn = SideColor.Red)
    {
        Pieces = pieces;
        Turn = turn;
    }

    /// <summary>
    /// Lấy thông tin quân cờ tại vị trí pos (nếu có và còn sống).
    /// </summary>
    public PieceState? GetPieceAt(Position pos)
    {
        if (Pieces.TryGetValue(pos, out var piece) && piece.IsAlive)
        {
            return piece;
        }
        return null;
    }

    /// <summary>
    /// Lấy danh sách toàn bộ quân cờ đang còn sống trên bàn cờ.
    /// </summary>
    public IEnumerable<PieceState> GetActivePieces()
    {
        return Pieces.Values.Where(p => p.IsAlive);
    }

    /// <summary>
    /// Lấy danh sách quân cờ còn sống theo phe (Red / Black).
    /// </summary>
    public IEnumerable<PieceState> GetActivePieces(SideColor side)
    {
        return Pieces.Values.Where(p => p.IsAlive && p.Side == side);
    }

    /// <summary>
    /// Thực hiện di chuyển quân từ position FROM sang TO, tạo ra một BoardState MỚI (Immutable).
    /// </summary>
    public BoardState ApplyMove(Position from, Position to)
    {
        var movingPiece = GetPieceAt(from);
        if (movingPiece == null) return this;

        var builder = Pieces.ToBuilder();
        builder.Remove(from);

        // Cập nhật quân di chuyển sang vị trí mới
        var updatedPiece = movingPiece.WithPosition(to);
        builder[to] = updatedPiece;

        // Đổi lượt đi sang phe đối phương
        var nextTurn = (Turn == SideColor.Red) ? SideColor.Black : SideColor.Red;

        return new BoardState(builder.ToImmutable(), nextTurn);
    }

    /// <summary>
    /// Tạo bàn cờ ban đầu chuẩn 32 quân cờ Tướng theo Canonical Coordinate & Canonical PieceIds.
    /// BLACK ở trên (y=0..3), RED ở dưới (y=6..9).
    /// </summary>
    public static BoardState CreateInitialBoard(SideColor turn = SideColor.Red)
    {
        var pieces = new List<PieceState>();

        // ---------------- BLACK PIECES (Top: y=0, 2, 3) ----------------
        pieces.Add(new PieceState("BLACK_CHARIOT_1", PieceType.Chariot, SideColor.Black, new Position(0, 0)));
        pieces.Add(new PieceState("BLACK_HORSE_1", PieceType.Horse, SideColor.Black, new Position(1, 0)));
        pieces.Add(new PieceState("BLACK_ELEPHANT_1", PieceType.Elephant, SideColor.Black, new Position(2, 0)));
        pieces.Add(new PieceState("BLACK_ADVISOR_1", PieceType.Advisor, SideColor.Black, new Position(3, 0)));
        pieces.Add(new PieceState("BLACK_GENERAL", PieceType.General, SideColor.Black, new Position(4, 0)));
        pieces.Add(new PieceState("BLACK_ADVISOR_2", PieceType.Advisor, SideColor.Black, new Position(5, 0)));
        pieces.Add(new PieceState("BLACK_ELEPHANT_2", PieceType.Elephant, SideColor.Black, new Position(6, 0)));
        pieces.Add(new PieceState("BLACK_HORSE_2", PieceType.Horse, SideColor.Black, new Position(7, 0)));
        pieces.Add(new PieceState("BLACK_CHARIOT_2", PieceType.Chariot, SideColor.Black, new Position(8, 0)));

        pieces.Add(new PieceState("BLACK_CANNON_1", PieceType.Cannon, SideColor.Black, new Position(1, 2)));
        pieces.Add(new PieceState("BLACK_CANNON_2", PieceType.Cannon, SideColor.Black, new Position(7, 2)));

        pieces.Add(new PieceState("BLACK_PAWN_1", PieceType.Pawn, SideColor.Black, new Position(0, 3)));
        pieces.Add(new PieceState("BLACK_PAWN_2", PieceType.Pawn, SideColor.Black, new Position(2, 3)));
        pieces.Add(new PieceState("BLACK_PAWN_3", PieceType.Pawn, SideColor.Black, new Position(4, 3)));
        pieces.Add(new PieceState("BLACK_PAWN_4", PieceType.Pawn, SideColor.Black, new Position(6, 3)));
        pieces.Add(new PieceState("BLACK_PAWN_5", PieceType.Pawn, SideColor.Black, new Position(8, 3)));

        // ---------------- RED PIECES (Bottom: y=9, 7, 6) ----------------
        pieces.Add(new PieceState("RED_CHARIOT_1", PieceType.Chariot, SideColor.Red, new Position(0, 9)));
        pieces.Add(new PieceState("RED_HORSE_1", PieceType.Horse, SideColor.Red, new Position(1, 9)));
        pieces.Add(new PieceState("RED_ELEPHANT_1", PieceType.Elephant, SideColor.Red, new Position(2, 9)));
        pieces.Add(new PieceState("RED_ADVISOR_1", PieceType.Advisor, SideColor.Red, new Position(3, 9)));
        pieces.Add(new PieceState("RED_GENERAL", PieceType.General, SideColor.Red, new Position(4, 9)));
        pieces.Add(new PieceState("RED_ADVISOR_2", PieceType.Advisor, SideColor.Red, new Position(5, 9)));
        pieces.Add(new PieceState("RED_ELEPHANT_2", PieceType.Elephant, SideColor.Red, new Position(6, 9)));
        pieces.Add(new PieceState("RED_HORSE_2", PieceType.Horse, SideColor.Red, new Position(7, 9)));
        pieces.Add(new PieceState("RED_CHARIOT_2", PieceType.Chariot, SideColor.Red, new Position(8, 9)));

        pieces.Add(new PieceState("RED_CANNON_1", PieceType.Cannon, SideColor.Red, new Position(1, 7)));
        pieces.Add(new PieceState("RED_CANNON_2", PieceType.Cannon, SideColor.Red, new Position(7, 7)));

        pieces.Add(new PieceState("RED_PAWN_1", PieceType.Pawn, SideColor.Red, new Position(0, 6)));
        pieces.Add(new PieceState("RED_PAWN_2", PieceType.Pawn, SideColor.Red, new Position(2, 6)));
        pieces.Add(new PieceState("RED_PAWN_3", PieceType.Pawn, SideColor.Red, new Position(4, 6)));
        pieces.Add(new PieceState("RED_PAWN_4", PieceType.Pawn, SideColor.Red, new Position(6, 6)));
        pieces.Add(new PieceState("RED_PAWN_5", PieceType.Pawn, SideColor.Red, new Position(8, 6)));

        var dictionary = pieces.ToImmutableDictionary(p => p.Position, p => p);
        return new BoardState(dictionary, turn);
    }
}
