using XiangqiOnline.RuleEngine.Attacks;
using XiangqiOnline.RuleEngine.Models;
using XiangqiOnline.RuleEngine.Tests.Fixtures;
using XiangqiOnline.Shared.Enums;
using XiangqiOnline.Shared.Models;

namespace XiangqiOnline.RuleEngine.Tests.Attacks;

public class AttackDetectorTests
{
    private static readonly Position Target = new(4, 4);

    [Fact]
    public void IsSquareAttacked_ShouldReturnTrue_WhenMatchingRuleAttacksTarget()
    {
        var piece = Piece("RED_CHARIOT", PieceType.Chariot, SideColor.Red, 0, 0);
        var board = BoardSetupFixture.CreateBoardWithPieces(piece);
        var detector = Detector(Rule(PieceType.Chariot, (_, attacker, target) =>
            attacker.Id == piece.Id && target == Target));

        Assert.True(detector.IsSquareAttacked(board, Target, SideColor.Red));
    }

    [Fact]
    public void IsSquareAttacked_ShouldReturnFalse_WhenMatchingRuleDoesNotAttackTarget()
    {
        var board = BoardSetupFixture.CreateBoardWithPieces(
            Piece("RED_CHARIOT", PieceType.Chariot, SideColor.Red, 0, 0));
        var detector = Detector(Rule(PieceType.Chariot, (_, _, _) => false));

        Assert.False(detector.IsSquareAttacked(board, Target, SideColor.Red));
    }

    [Fact]
    public void AttackDetection_ShouldNotDependOnBoardTurn()
    {
        var piece = Piece("RED_CHARIOT", PieceType.Chariot, SideColor.Red, 0, 0);
        var redTurnBoard = BoardSetupFixture.CreateBoardWithPieces(SideColor.Red, piece);
        var blackTurnBoard = BoardSetupFixture.CreateBoardWithPieces(SideColor.Black, piece);
        var detector = Detector(Rule(PieceType.Chariot, (_, _, _) => true));

        var duringRedTurn = detector.IsSquareAttacked(redTurnBoard, Target, SideColor.Red);
        var duringBlackTurn = detector.IsSquareAttacked(blackTurnBoard, Target, SideColor.Red);

        Assert.True(duringRedTurn);
        Assert.Equal(duringRedTurn, duringBlackTurn);
    }

    [Fact]
    public void FindAttackers_ShouldFilterByAttackingSide()
    {
        var red = Piece("RED_CHARIOT", PieceType.Chariot, SideColor.Red, 0, 0);
        var black = Piece("BLACK_CHARIOT", PieceType.Chariot, SideColor.Black, 8, 9);
        var board = BoardSetupFixture.CreateBoardWithPieces(red, black);
        var detector = Detector(Rule(PieceType.Chariot, (_, _, _) => true));

        var attackers = detector.FindAttackers(board, Target, SideColor.Red);

        Assert.Collection(attackers, attacker => Assert.Same(red, attacker));
    }

    [Fact]
    public void FindAttackers_ShouldIgnoreCapturedOrInactivePieces()
    {
        var active = Piece("ACTIVE", PieceType.Pawn, SideColor.Red, 0, 0);
        var captured = Piece("CAPTURED", PieceType.Pawn, SideColor.Red, 1, 0, isAlive: false);
        var board = BoardSetupFixture.CreateBoardWithPieces(active, captured);
        var detector = Detector(Rule(PieceType.Pawn, (_, _, _) => true));

        var attackers = detector.FindAttackers(board, Target, SideColor.Red);

        Assert.Collection(attackers, attacker => Assert.Same(active, attacker));
    }

    [Fact]
    public void FindAttackers_ShouldDispatchUsingMatchingPieceType()
    {
        var chariot = Piece("CHARIOT", PieceType.Chariot, SideColor.Red, 0, 0);
        var horse = Piece("HORSE", PieceType.Horse, SideColor.Red, 1, 0);
        var board = BoardSetupFixture.CreateBoardWithPieces(chariot, horse);
        var chariotCalls = 0;
        var horseCalls = 0;
        var detector = Detector(
            Rule(PieceType.Chariot, (_, attacker, _) =>
            {
                chariotCalls++;
                return attacker.Type == PieceType.Chariot;
            }),
            Rule(PieceType.Horse, (_, attacker, _) =>
            {
                horseCalls++;
                return attacker.Type == PieceType.Horse;
            }));

        var attackers = detector.FindAttackers(board, Target, SideColor.Red);

        Assert.Equal(2, attackers.Count);
        Assert.Equal(1, chariotCalls);
        Assert.Equal(1, horseCalls);
    }

    [Fact]
    public void FindAttackers_ShouldReturnAllMatchingAttackers()
    {
        var first = Piece("FIRST", PieceType.Cannon, SideColor.Black, 0, 0);
        var second = Piece("SECOND", PieceType.Cannon, SideColor.Black, 1, 0);
        var nonAttacker = Piece("THIRD", PieceType.Cannon, SideColor.Black, 2, 0);
        var board = BoardSetupFixture.CreateBoardWithPieces(first, second, nonAttacker);
        var detector = Detector(Rule(PieceType.Cannon, (_, attacker, _) => attacker != nonAttacker));

        var attackers = detector.FindAttackers(board, Target, SideColor.Black);

        Assert.Equal(new[] { first, second }, attackers);
    }

    [Fact]
    public void FindAttackers_ShouldReturnDeterministicOrderByPieceId()
    {
        var pieceZ = Piece("Z", PieceType.Pawn, SideColor.Red, 0, 0);
        var pieceLowerA = Piece("a", PieceType.Pawn, SideColor.Red, 1, 0);
        var pieceUpperA = Piece("A", PieceType.Pawn, SideColor.Red, 2, 0);
        var board = BoardSetupFixture.CreateBoardWithPieces(pieceZ, pieceLowerA, pieceUpperA);
        var detector = Detector(Rule(PieceType.Pawn, (_, _, _) => true));

        var attackers = detector.FindAttackers(board, Target, SideColor.Red);

        Assert.Equal(new[] { "A", "Z", "a" }, attackers.Select(attacker => attacker.Id));
    }

    [Fact]
    public void Constructor_ShouldRejectDuplicateRulesForSamePieceType()
    {
        var first = Rule(PieceType.General, (_, _, _) => true);
        var duplicate = Rule(PieceType.General, (_, _, _) => false);

        var exception = Assert.Throws<ArgumentException>(() => Detector(first, duplicate));

        Assert.Contains(nameof(PieceType.General), exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void FindAttackers_ShouldFailFast_WhenActivePieceHasNoRegisteredAttackRule()
    {
        var piece = Piece("UNSUPPORTED_HORSE", PieceType.Horse, SideColor.Red, 0, 0);
        var board = BoardSetupFixture.CreateBoardWithPieces(piece);
        var detector = Detector(Rule(PieceType.Chariot, (_, _, _) => true));

        var exception = Assert.Throws<InvalidOperationException>(() =>
            detector.FindAttackers(board, Target, SideColor.Red));

        Assert.Contains(piece.Id, exception.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(PieceType.Horse), exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(9, 0)]
    [InlineData(0, -1)]
    [InlineData(0, 10)]
    public void IsSquareAttacked_ShouldRejectOutOfBoardTarget(int x, int y)
    {
        var detector = Detector();
        var board = BoardSetupFixture.CreateEmptyBoard();

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            detector.IsSquareAttacked(board, new Position(x, y), SideColor.Red));
    }

    private static AttackDetector Detector(params IAttackRule[] rules) => new(rules);

    private static PredicateAttackRule Rule(
        PieceType pieceType,
        Func<BoardState, PieceState, Position, bool> predicate) => new(pieceType, predicate);

    private static PieceState Piece(
        string id,
        PieceType type,
        SideColor side,
        int x,
        int y,
        bool isAlive = true) => new(id, type, side, new Position(x, y), isAlive);

    private sealed class PredicateAttackRule(
        PieceType matchingPieceType,
        Func<BoardState, PieceState, Position, bool> predicate) : IAttackRule
    {
        public PieceType MatchingPieceType { get; } = matchingPieceType;

        public bool CanAttack(BoardState board, PieceState attacker, Position target) =>
            predicate(board, attacker, target);
    }
}
