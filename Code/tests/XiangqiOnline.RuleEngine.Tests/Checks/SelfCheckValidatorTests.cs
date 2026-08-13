using XiangqiOnline.RuleEngine.Attacks;
using XiangqiOnline.RuleEngine.Checks;
using XiangqiOnline.RuleEngine.Models;
using XiangqiOnline.RuleEngine.Pipeline;
using XiangqiOnline.Shared.Enums;
using XiangqiOnline.Shared.Models;

namespace XiangqiOnline.RuleEngine.Tests.Checks;

public class SelfCheckValidatorTests
{
    private readonly SelfCheckValidator _validator = CheckTestFactory.CreateSelfCheckValidator();

    [Fact]
    public void Validate_ShouldReturnSelfCheck_WhenMoveExposesOwnGeneral()
    {
        var blocker = Piece("RED_BLOCKER", PieceType.Chariot, SideColor.Red, 4, 5);
        var board = Board(
            General(SideColor.Red, 4, 9), blocker,
            Piece("BLACK_CHARIOT", PieceType.Chariot, SideColor.Black, 4, 0),
            General(SideColor.Black, 3, 0));
        AssertError(ErrorCodes.SELF_CHECK, board, blocker, new Position(5, 5));
    }

    [Fact]
    public void Validate_ShouldReturnCheckNotResolved_WhenAlreadyCheckedAndMoveIsIrrelevant()
    {
        var pawn = Piece("RED_PAWN", PieceType.Pawn, SideColor.Red, 0, 6);
        var board = Board(
            General(SideColor.Red, 4, 9), pawn,
            Piece("BLACK_CHARIOT", PieceType.Chariot, SideColor.Black, 4, 0),
            General(SideColor.Black, 3, 0));
        AssertError(ErrorCodes.CHECK_NOT_RESOLVED, board, pawn, new Position(0, 5));
    }

    [Fact]
    public void Validate_ShouldSucceed_WhenGeneralMovesOutOfCheck()
    {
        var redGeneral = General(SideColor.Red, 4, 9);
        var board = Board(redGeneral, Piece("BLACK_CHARIOT", PieceType.Chariot, SideColor.Black, 4, 0));
        Assert.True(_validator.Validate(board, redGeneral, new Position(3, 9)).IsValid);
    }

    [Fact]
    public void Validate_ShouldSucceed_WhenMovingPieceCapturesChecker()
    {
        var defender = Piece("RED_CHARIOT", PieceType.Chariot, SideColor.Red, 3, 8);
        var board = Board(
            General(SideColor.Red, 4, 9), defender,
            Piece("BLACK_CHARIOT", PieceType.Chariot, SideColor.Black, 4, 8));
        Assert.True(_validator.Validate(board, defender, new Position(4, 8)).IsValid);
    }

    [Fact]
    public void Validate_ShouldSucceed_WhenMovingPieceBlocksLineCheck()
    {
        var defender = Piece("RED_CHARIOT", PieceType.Chariot, SideColor.Red, 3, 5);
        var board = Board(
            General(SideColor.Red, 4, 9), defender,
            Piece("BLACK_CHARIOT", PieceType.Chariot, SideColor.Black, 4, 0));
        Assert.True(_validator.Validate(board, defender, new Position(4, 5)).IsValid);
    }

    [Fact]
    public void Validate_ShouldSucceed_WhenCannonScreenMovesAway()
    {
        var screen = Piece("RED_SCREEN", PieceType.Chariot, SideColor.Red, 1, 4);
        var board = Board(
            General(SideColor.Red, 1, 6), screen,
            Piece("BLACK_CANNON", PieceType.Cannon, SideColor.Black, 1, 2));
        Assert.True(_validator.Validate(board, screen, new Position(2, 4)).IsValid);
    }

    [Fact]
    public void Validate_ShouldReturnSelfCheck_WhenMoveCreatesCannonScreen()
    {
        var movingPiece = Piece("RED_CHARIOT", PieceType.Chariot, SideColor.Red, 2, 4);
        var board = Board(
            General(SideColor.Red, 1, 6), movingPiece,
            Piece("BLACK_CANNON", PieceType.Cannon, SideColor.Black, 1, 2));
        AssertError(ErrorCodes.SELF_CHECK, board, movingPiece, new Position(1, 4));
    }

    [Fact]
    public void Validate_ShouldReturnSelfCheck_WhenMoveUnblocksHorseLeg()
    {
        var legBlocker = Piece("RED_BLOCKER", PieceType.Chariot, SideColor.Red, 3, 8);
        var board = Board(
            General(SideColor.Red, 4, 9), legBlocker,
            Piece("BLACK_HORSE", PieceType.Horse, SideColor.Black, 3, 7));
        AssertError(ErrorCodes.SELF_CHECK, board, legBlocker, new Position(2, 8));
    }

    [Fact]
    public void Validate_ShouldPrioritizeGeneralsFacing_WhenBlockerMovesAway()
    {
        var blocker = Piece("RED_BLOCKER", PieceType.Chariot, SideColor.Red, 4, 5);
        var board = Board(General(SideColor.Black, 4, 0), blocker, General(SideColor.Red, 4, 9));
        AssertError(ErrorCodes.GENERALS_FACING, board, blocker, new Position(5, 5));
    }

    [Fact]
    public void Validate_ShouldSucceed_WhenMoveResolvesFlyingGeneralByAddingBlocker()
    {
        var blocker = Piece("RED_BLOCKER", PieceType.Chariot, SideColor.Red, 3, 5);
        var board = Board(General(SideColor.Black, 4, 0), blocker, General(SideColor.Red, 4, 9));
        Assert.True(_validator.Validate(board, blocker, new Position(4, 5)).IsValid);
    }

    [Fact]
    public void Validate_ShouldNotMutateOriginalBoard()
    {
        var blocker = Piece("RED_BLOCKER", PieceType.Chariot, SideColor.Red, 4, 5);
        var board = CheckTestFactory.Board(
            SideColor.Red,
            General(SideColor.Red, 4, 9), blocker,
            Piece("BLACK_CHARIOT", PieceType.Chariot, SideColor.Black, 4, 0),
            General(SideColor.Black, 3, 0));
        var originalPieces = board.Pieces;
        var originalTurn = board.Turn;

        _validator.Validate(board, blocker, new Position(5, 5));

        Assert.Same(originalPieces, board.Pieces);
        Assert.Equal(originalTurn, board.Turn);
        Assert.Equal(blocker, board.GetPieceAt(new Position(4, 5)));
        Assert.Null(board.GetPieceAt(new Position(5, 5)));
    }

    [Fact]
    public void Validate_ShouldNotDependOnBoardTurn()
    {
        var blocker = Piece("RED_BLOCKER", PieceType.Chariot, SideColor.Red, 4, 5);
        var pieces = new[]
        {
            General(SideColor.Red, 4, 9), blocker,
            Piece("BLACK_CHARIOT", PieceType.Chariot, SideColor.Black, 4, 0),
            General(SideColor.Black, 3, 0)
        };
        var redTurn = _validator.Validate(CheckTestFactory.Board(SideColor.Red, pieces), blocker, new Position(5, 5));
        var blackTurn = _validator.Validate(CheckTestFactory.Board(SideColor.Black, pieces), blocker, new Position(5, 5));
        Assert.Equal(redTurn, blackTurn);
    }

    [Fact]
    public void MovementPipelineAndSelfCheckBothRejectIllegalMove()
    {
        var blocker = Piece("RED_BLOCKER", PieceType.Chariot, SideColor.Red, 4, 5);
        var board = CheckTestFactory.Board(
            SideColor.Red,
            General(SideColor.Red, 4, 9), blocker,
            Piece("BLACK_CHARIOT", PieceType.Chariot, SideColor.Black, 4, 0),
            General(SideColor.Black, 3, 0));
        var target = new Position(5, 5);

        Assert.Equal(ErrorCodes.SELF_CHECK, new MoveValidationPipeline().Validate(board, Intent(blocker.Position, target)).ErrorCode);
        Assert.Equal(ErrorCodes.SELF_CHECK, _validator.Validate(board, blocker, target).ErrorCode);
    }

    [Fact]
    public void MovementPipelineAndSelfCheckCanBothPass()
    {
        var chariot = Piece("RED_CHARIOT", PieceType.Chariot, SideColor.Red, 0, 9);
        var board = CheckTestFactory.Board(
            SideColor.Red,
            General(SideColor.Red, 4, 9), chariot,
            General(SideColor.Black, 3, 0));
        var target = new Position(0, 8);

        Assert.True(new MoveValidationPipeline().Validate(board, Intent(chariot.Position, target)).IsValid);
        Assert.True(_validator.Validate(board, chariot, target).IsValid);
    }

    [Fact]
    public void Constructor_ShouldRejectNullCheckDetector()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new SelfCheckValidator(null!, new GeneralsFacingDetector()));
    }

    [Fact]
    public void Constructor_ShouldRejectNullGeneralsFacingDetector()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new SelfCheckValidator(CheckTestFactory.CreateCheckDetector(), null!));
    }

    [Fact]
    public void Validate_ShouldRejectNullBoard()
    {
        var piece = Piece("PIECE", PieceType.Pawn, SideColor.Red, 0, 6);
        Assert.Throws<ArgumentNullException>(() => _validator.Validate(null!, piece, new Position(0, 5)));
    }

    [Fact]
    public void Validate_ShouldRejectNullMovingPiece()
    {
        Assert.Throws<ArgumentNullException>(() =>
            _validator.Validate(CheckTestFactory.Board(), null!, new Position(0, 5)));
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(9, 0)]
    [InlineData(0, -1)]
    [InlineData(0, 10)]
    public void Validate_ShouldRejectOutOfBoardTarget(int x, int y)
    {
        var piece = Piece("PIECE", PieceType.Pawn, SideColor.Red, 0, 6);
        var board = Board(piece, General(SideColor.Red, 4, 9));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            _validator.Validate(board, piece, new Position(x, y)));
    }

    [Fact]
    public void Validate_ShouldThrow_WhenMovingPieceIsNotActiveAtSource()
    {
        var piece = Piece("PIECE", PieceType.Pawn, SideColor.Red, 0, 6, false);
        var board = Board(piece, General(SideColor.Red, 4, 9));
        Assert.Throws<InvalidOperationException>(() =>
            _validator.Validate(board, piece, new Position(0, 5)));
    }

    [Fact]
    public void Validate_ShouldThrow_WhenPieceIdDoesNotMatchActiveSourcePiece()
    {
        var boardPiece = Piece("BOARD_PIECE", PieceType.Pawn, SideColor.Red, 0, 6);
        var suppliedPiece = boardPiece with { Id = "OTHER_ID" };
        var board = Board(boardPiece, General(SideColor.Red, 4, 9));
        Assert.Throws<InvalidOperationException>(() =>
            _validator.Validate(board, suppliedPiece, new Position(0, 5)));
    }

    [Fact]
    public void Validate_ShouldThrow_WhenSuppliedPieceSideDoesNotMatchBoardPiece()
    {
        var boardPiece = Piece("PIECE", PieceType.Pawn, SideColor.Red, 0, 6);
        var suppliedPiece = boardPiece with { Side = SideColor.Black };
        var board = Board(boardPiece, General(SideColor.Red, 4, 9));
        Assert.Throws<InvalidOperationException>(() =>
            _validator.Validate(board, suppliedPiece, new Position(0, 5)));
    }

    [Fact]
    public void Validate_ShouldThrow_WhenSuppliedPieceTypeDoesNotMatchBoardPiece()
    {
        var boardPiece = Piece("PIECE", PieceType.Pawn, SideColor.Red, 0, 6);
        var suppliedPiece = boardPiece with { Type = PieceType.Chariot };
        var board = Board(boardPiece, General(SideColor.Red, 4, 9));
        Assert.Throws<InvalidOperationException>(() =>
            _validator.Validate(board, suppliedPiece, new Position(0, 5)));
    }

    [Fact]
    public void Validate_ShouldThrow_WhenSuppliedPieceIsMarkedCapturedButBoardPieceIsActive()
    {
        var boardPiece = Piece("PIECE", PieceType.Pawn, SideColor.Red, 0, 6);
        var suppliedPiece = boardPiece with { IsAlive = false };
        var board = Board(boardPiece, General(SideColor.Red, 4, 9));
        Assert.Throws<InvalidOperationException>(() =>
            _validator.Validate(board, suppliedPiece, new Position(0, 5)));
    }

    [Fact]
    public void GeneralMovementRejected_WhenTargetSquareCausesSelfCheck()
    {
        var redGeneral = General(SideColor.Red, 4, 9);
        var board = CheckTestFactory.Board(
            SideColor.Red,
            redGeneral,
            Piece("BLACK_CHARIOT", PieceType.Chariot, SideColor.Black, 3, 0),
            General(SideColor.Black, 5, 0));
        var target = new Position(3, 9);

        Assert.Equal(ErrorCodes.SELF_CHECK, new MoveValidationPipeline().Validate(board, Intent(redGeneral.Position, target)).ErrorCode);
        Assert.Equal(ErrorCodes.SELF_CHECK, _validator.Validate(board, redGeneral, target).ErrorCode);
    }

    [Fact]
    public void Validate_ShouldReturnSelfCheck_ForBlackSideSymmetry()
    {
        var blocker = Piece("BLACK_BLOCKER", PieceType.Chariot, SideColor.Black, 4, 4);
        var board = Board(
            General(SideColor.Black, 4, 0), blocker,
            Piece("RED_CHARIOT", PieceType.Chariot, SideColor.Red, 4, 9),
            General(SideColor.Red, 3, 9));
        AssertError(ErrorCodes.SELF_CHECK, board, blocker, new Position(5, 4));
    }

    [Fact]
    public void Validate_ShouldReturnCheckNotResolved_WhenMoveRemovesOnlyOneDoubleChecker()
    {
        var defender = Piece("RED_CHARIOT", PieceType.Chariot, SideColor.Red, 2, 7);
        var horse = Piece("BLACK_HORSE", PieceType.Horse, SideColor.Black, 3, 7);
        var board = Board(
            General(SideColor.Red, 4, 9), defender,
            Piece("BLACK_CHARIOT", PieceType.Chariot, SideColor.Black, 4, 0), horse,
            General(SideColor.Black, 5, 0));
        AssertError(ErrorCodes.CHECK_NOT_RESOLVED, board, defender, horse.Position);
    }

    [Fact]
    public void Validate_CaptureSimulationShouldPreserveOriginalBoard()
    {
        var defender = Piece("RED_CHARIOT", PieceType.Chariot, SideColor.Red, 3, 8);
        var checker = Piece("BLACK_CHARIOT", PieceType.Chariot, SideColor.Black, 4, 8);
        var board = CheckTestFactory.Board(SideColor.Red, General(SideColor.Red, 4, 9), defender, checker);
        var originalTurn = board.Turn;

        Assert.True(_validator.Validate(board, defender, checker.Position).IsValid);

        Assert.Equal(defender, board.GetPieceAt(defender.Position));
        Assert.Equal(checker, board.GetPieceAt(checker.Position));
        Assert.Equal(originalTurn, board.Turn);
    }

    private void AssertError(string expected, BoardState board, PieceState piece, Position target) =>
        Assert.Equal(expected, _validator.Validate(board, piece, target).ErrorCode);

    private static MoveIntent Intent(Position from, Position to) => new("move", from, to, 0);

    private static BoardState Board(params PieceState[] pieces) => CheckTestFactory.Board(pieces);
    private static PieceState General(SideColor side, int x, int y) => CheckTestFactory.General(side, x, y);
    private static PieceState Piece(string id, PieceType type, SideColor side, int x, int y, bool alive = true) =>
        CheckTestFactory.Piece(id, type, side, x, y, alive);
}
