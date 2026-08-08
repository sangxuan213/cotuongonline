using XiangqiOnline.RuleEngine.Models;
using XiangqiOnline.RuleEngine.Pipeline;
using XiangqiOnline.Shared.Enums;
using XiangqiOnline.Shared.Models;
using Xunit;

namespace XiangqiOnline.RuleEngine.Tests;

public class MoveValidationPipelineTests
{
    private readonly MoveValidationPipeline _pipeline = new();

    [Fact]
    public void Pipeline_ValidMove_ShouldReturnSuccess()
    {
        var board = BoardState.CreateInitialBoard(); // Turn = Red
        var intent = new MoveIntent("move-1", new Position(0, 6), new Position(0, 5));

        var result = _pipeline.Validate(board, intent);

        Assert.True(result.IsValid);
        Assert.Equal(ErrorCodes.OK, result.ErrorCode);
    }

    [Fact]
    public void Pipeline_WrongTurn_ShouldReturnNotYourTurnError()
    {
        var board = BoardState.CreateInitialBoard(SideColor.Black); // Turn = Black
        // Red pawn at (0,6) trying to move when it's Black's turn
        var intent = new MoveIntent("move-2", new Position(0, 6), new Position(0, 5));

        var result = _pipeline.Validate(board, intent);

        Assert.False(result.IsValid);
        Assert.Equal(ErrorCodes.NOT_YOUR_TURN, result.ErrorCode);
    }

    [Fact]
    public void Pipeline_InvalidCoordinate_ShouldReturnInvalidCoordinateError()
    {
        var board = BoardState.CreateInitialBoard();
        var intent = new MoveIntent("move-3", new Position(-1, 0), new Position(0, 5));

        var result = _pipeline.Validate(board, intent);

        Assert.False(result.IsValid);
        Assert.Equal(ErrorCodes.OUT_OF_BOARD, result.ErrorCode);
    }

    [Fact]
    public void Pipeline_SameDestination_ShouldReturnSameDestinationError()
    {
        var board = BoardState.CreateInitialBoard();
        var intent = new MoveIntent("move-4", new Position(0, 6), new Position(0, 6));

        var result = _pipeline.Validate(board, intent);

        Assert.False(result.IsValid);
        Assert.Equal(ErrorCodes.INVALID_GEOMETRY, result.ErrorCode);
    }

    [Fact]
    public void Pipeline_NoPieceAtSource_ShouldReturnNoPieceAtSourceError()
    {
        var board = BoardState.CreateInitialBoard();
        var intent = new MoveIntent("move-5", new Position(0, 5), new Position(0, 4));

        var result = _pipeline.Validate(board, intent);

        Assert.False(result.IsValid);
        Assert.Equal(ErrorCodes.NO_PIECE_AT_SOURCE, result.ErrorCode);
    }

    [Fact]
    public void Pipeline_NotYourPieceWhenCorrectTurn_ShouldReturnNotYourTurnError()
    {
        var board = BoardState.CreateInitialBoard(); // Turn = Red
        // Black pawn at (0,3) trying to move when it's Red's turn
        var intent = new MoveIntent("move-6", new Position(0, 3), new Position(0, 4));

        var result = _pipeline.Validate(board, intent);

        Assert.False(result.IsValid);
        Assert.Equal(ErrorCodes.NOT_YOUR_TURN, result.ErrorCode);
    }

    [Fact]
    public void Pipeline_DestinationOccupiedByFriend_ShouldReturnFriendlyError()
    {
        var board = BoardState.CreateInitialBoard();
        var intent = new MoveIntent("move-7", new Position(0, 9), new Position(0, 6)); // Red Chariot moving to friendly Pawn at (0,6)

        var result = _pipeline.Validate(board, intent);

        Assert.False(result.IsValid);
        Assert.Equal(ErrorCodes.ALLY_AT_DESTINATION, result.ErrorCode);
    }

    [Fact]
    public void Pipeline_PieceSpecificObstacle_ShouldReturnPathBlockedError()
    {
        var board = BoardState.CreateInitialBoard();
        var intent = new MoveIntent("move-8", new Position(0, 9), new Position(0, 5)); // Red Chariot over (0,6) Pawn

        var result = _pipeline.Validate(board, intent);

        Assert.False(result.IsValid);
        Assert.Equal(ErrorCodes.PATH_BLOCKED, result.ErrorCode);
    }

    [Fact]
    public void Pipeline_HorseLegBlocked_ShouldReturnHorseLegBlockedError()
    {
        var board = BoardState.CreateInitialBoard();
        var b = board.Pieces.ToBuilder();
        b[new Position(1, 8)] = new PieceState("P", PieceType.Pawn, SideColor.Red, new Position(1, 8));
        var customBoard = new BoardState(b.ToImmutable(), SideColor.Red);

        var intent = new MoveIntent("move-9", new Position(1, 9), new Position(2, 7));

        var result = _pipeline.Validate(customBoard, intent);

        Assert.False(result.IsValid);
        Assert.Equal(ErrorCodes.HORSE_LEG_BLOCKED, result.ErrorCode);
    }
}
