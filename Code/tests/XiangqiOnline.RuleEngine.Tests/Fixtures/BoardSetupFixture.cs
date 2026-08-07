using System.Collections.Immutable;
using XiangqiOnline.RuleEngine.Models;
using XiangqiOnline.Shared.Enums;
using XiangqiOnline.Shared.Models;

namespace XiangqiOnline.RuleEngine.Tests.Fixtures;

/// <summary>
/// Fixture hỗ trợ dựng các thế cờ thử nghiệm mẫu cho TV3 & TV4.
/// </summary>
public static class BoardSetupFixture
{
    public static BoardState CreateEmptyBoard(SideColor turn = SideColor.Red)
    {
        return new BoardState(ImmutableDictionary<Position, PieceState>.Empty, turn);
    }

    public static BoardState CreateBoardWithPieces(params PieceState[] pieces)
    {
        var dictionary = pieces.ToDictionary(p => p.Position, p => p).ToImmutableDictionary();
        return new BoardState(dictionary, SideColor.Red);
    }
}
