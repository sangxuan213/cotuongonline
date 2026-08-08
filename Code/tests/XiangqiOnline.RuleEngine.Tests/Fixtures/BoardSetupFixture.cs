using System.Collections.Immutable;
using XiangqiOnline.RuleEngine.Models;
using XiangqiOnline.Shared.Enums;
using XiangqiOnline.Shared.Models;

namespace XiangqiOnline.RuleEngine.Tests.Fixtures;

/// <summary>
/// Fixture hỗ trợ dựng các thế cờ thử nghiệm linh hoạt cho TV3 & TV4.
/// </summary>
public static class BoardSetupFixture
{
    public static BoardState CreateEmptyBoard(SideColor turn = SideColor.Red)
    {
        return new BoardState(ImmutableDictionary<Position, PieceState>.Empty, turn);
    }

    public static BoardState CreateBoardWithPieces(SideColor turn, params PieceState[] pieces)
    {
        var dictionary = pieces.ToDictionary(p => p.Position, p => p).ToImmutableDictionary();
        return new BoardState(dictionary, turn);
    }

    public static BoardState CreateBoardWithPieces(params PieceState[] pieces)
    {
        return CreateBoardWithPieces(SideColor.Red, pieces);
    }

    public static BoardState CreateBoardWithGenerals(SideColor turn = SideColor.Red)
    {
        var redGen = new PieceState("RED_GENERAL", PieceType.General, SideColor.Red, new Position(4, 9));
        var blackGen = new PieceState("BLACK_GENERAL", PieceType.General, SideColor.Black, new Position(4, 0));
        return CreateBoardWithPieces(turn, redGen, blackGen);
    }
}
