using XiangqiOnline.RuleEngine.Attacks;
using XiangqiOnline.RuleEngine.Models;
using XiangqiOnline.RuleEngine.Tests.Fixtures;
using XiangqiOnline.Shared.Enums;
using XiangqiOnline.Shared.Models;

namespace XiangqiOnline.RuleEngine.Tests.Attacks;

public class GeneralsFacingDetectorTests
{
    private readonly GeneralsFacingDetector _detector = new();

    [Fact]
    public void AreGeneralsFacing_ShouldReturnTrue_WhenSameFileAndNoBlocker() =>
        Assert.True(_detector.AreGeneralsFacing(BoardWithGenerals(4, 4)));

    [Fact]
    public void AreGeneralsFacing_ShouldReturnFalse_WhenDifferentFiles()
    {
        var board = BoardSetupFixture.CreateBoardWithPieces(General(SideColor.Black, 3, 0), General(SideColor.Red, 4, 9));
        Assert.False(_detector.AreGeneralsFacing(board));
    }

    [Fact]
    public void AreGeneralsFacing_ShouldReturnFalse_WhenBlockedByBlackPiece() =>
        Assert.False(_detector.AreGeneralsFacing(BoardWithGenerals(4, 4, Blocker(SideColor.Black, 4, 5))));

    [Fact]
    public void AreGeneralsFacing_ShouldReturnFalse_WhenBlockedByRedPiece() =>
        Assert.False(_detector.AreGeneralsFacing(BoardWithGenerals(4, 4, Blocker(SideColor.Red, 4, 5))));

    [Fact]
    public void AreGeneralsFacing_ShouldIgnoreCapturedBlocker() =>
        Assert.True(_detector.AreGeneralsFacing(BoardWithGenerals(4, 4, Blocker(SideColor.Red, 4, 5, false))));

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    [InlineData(8)]
    public void AreGeneralsFacing_ShouldWorkOnEveryColumn(int x) =>
        Assert.True(_detector.AreGeneralsFacing(BoardWithGenerals(x, x)));

    [Fact]
    public void AreGeneralsFacing_ShouldNotDependOnBoardTurn()
    {
        var redTurn = BoardWithGenerals(4, 4, SideColor.Red);
        var blackTurn = BoardWithGenerals(4, 4, SideColor.Black);
        Assert.Equal(_detector.AreGeneralsFacing(redTurn), _detector.AreGeneralsFacing(blackTurn));
    }

    [Fact]
    public void AreGeneralsFacing_ShouldReturnFalse_WhenRedGeneralMissing()
    {
        var board = BoardSetupFixture.CreateBoardWithPieces(General(SideColor.Black, 4, 0));
        Assert.False(_detector.AreGeneralsFacing(board));
    }

    [Fact]
    public void AreGeneralsFacing_ShouldReturnFalse_WhenBlackGeneralMissing()
    {
        var board = BoardSetupFixture.CreateBoardWithPieces(General(SideColor.Red, 4, 9));
        Assert.False(_detector.AreGeneralsFacing(board));
    }

    [Theory]
    [InlineData(SideColor.Red)]
    [InlineData(SideColor.Black)]
    public void AreGeneralsFacing_ShouldThrow_WhenSideHasDuplicateActiveGenerals(SideColor duplicateSide)
    {
        var pieces = new List<PieceState>
        {
            General(SideColor.Black, 4, 0),
            General(SideColor.Red, 4, 9),
            new($"{duplicateSide}_GENERAL_2", PieceType.General, duplicateSide, new Position(3, duplicateSide == SideColor.Black ? 1 : 8))
        };

        Assert.Throws<InvalidOperationException>(() =>
            _detector.AreGeneralsFacing(BoardSetupFixture.CreateBoardWithPieces([.. pieces])));
    }

    [Fact]
    public void AreGeneralsFacing_ShouldThrow_WhenBoardIsNull() =>
        Assert.Throws<ArgumentNullException>(() => _detector.AreGeneralsFacing(null!));

    private static BoardState BoardWithGenerals(int blackX, int redX, params PieceState[] others) =>
        BoardSetupFixture.CreateBoardWithPieces([General(SideColor.Black, blackX, 0), General(SideColor.Red, redX, 9), .. others]);

    private static BoardState BoardWithGenerals(int blackX, int redX, SideColor turn) =>
        BoardSetupFixture.CreateBoardWithPieces(turn, General(SideColor.Black, blackX, 0), General(SideColor.Red, redX, 9));

    private static PieceState General(SideColor side, int x, int y) =>
        new($"{side}_GENERAL", PieceType.General, side, new Position(x, y));

    private static PieceState Blocker(SideColor side, int x, int y, bool alive = true) =>
        new("BLOCKER", PieceType.Pawn, side, new Position(x, y), alive);
}
