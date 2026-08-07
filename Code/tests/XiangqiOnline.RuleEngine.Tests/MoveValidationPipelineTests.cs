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
        var intent = new MoveIntent(SideColor.Red, new Position(0, 3), new Position(0, 4));

        var result = _pipeline.Validate(board, intent);

        Assert.True(result.IsValid);
        Assert.Equal(ErrorCodes.OK, result.ErrorCode);
    }

    [Fact]
    public void Pipeline_WrongTurn_ShouldReturnNotYourTurnError()
    {
        var board = BoardState.CreateInitialBoard(); // Turn = Red
        var intent = new MoveIntent(SideColor.Black, new Position(0, 6), new Position(0, 5));

        var result = _pipeline.Validate(board, intent);

        Assert.False(result.IsValid);
        Assert.Equal(ErrorCodes.NOT_YOUR_TURN, result.ErrorCode);
    }

    [Fact]
    public void Pipeline_InvalidCoordinate_ShouldReturnInvalidCoordinateError()
    {
        var board = BoardState.CreateInitialBoard();
        var intent = new MoveIntent(SideColor.Red, new Position(-1, 0), new Position(0, 4));

        var result = _pipeline.Validate(board, intent);

        Assert.False(result.IsValid);
        Assert.Equal(ErrorCodes.INVALID_COORDINATE, result.ErrorCode);
    }

    [Fact]
    public void Pipeline_SameDestination_ShouldReturnSameDestinationError()
    {
        var board = BoardState.CreateInitialBoard();
        var intent = new MoveIntent(SideColor.Red, new Position(0, 3), new Position(0, 3));

        var result = _pipeline.Validate(board, intent);

        Assert.False(result.IsValid);
        Assert.Equal(ErrorCodes.SAME_DESTINATION, result.ErrorCode);
    }

    [Fact]
    public void Pipeline_NoPieceAtSource_ShouldReturnNoPieceAtSourceError()
    {
        var board = BoardState.CreateInitialBoard();
        var intent = new MoveIntent(SideColor.Red, new Position(0, 1), new Position(0, 2));

        var result = _pipeline.Validate(board, intent);

        Assert.False(result.IsValid);
        Assert.Equal(ErrorCodes.NO_PIECE_AT_SOURCE, result.ErrorCode);
    }

    [Fact]
    public void Pipeline_NotYourPieceWhenCorrectTurn_ShouldReturnNotYourPieceError()
    {
        var board = BoardState.CreateInitialBoard();
        // Red player trying to move Black pawn at (0,6)
        var intent = new MoveIntent(SideColor.Red, new Position(0, 6), new Position(0, 5));

        var result = _pipeline.Validate(board, intent);

        Assert.False(result.IsValid);
        Assert.Equal(ErrorCodes.NOT_YOUR_PIECE, result.ErrorCode);
    }

    [Fact]
    public void Pipeline_DestinationOccupiedByFriend_ShouldReturnFriendlyError()
    {
        var board = BoardState.CreateInitialBoard();
        var intent = new MoveIntent(SideColor.Red, new Position(0, 0), new Position(0, 3)); // Rook moving to friendly Pawn at (0,3)

        var result = _pipeline.Validate(board, intent);

        Assert.False(result.IsValid);
        Assert.Equal(ErrorCodes.DESTINATION_OCCUPIED_BY_FRIEND, result.ErrorCode);
    }

    [Fact]
    public void Pipeline_PieceSpecificObstacle_ShouldReturnPathBlockedError()
    {
        var board = BoardState.CreateInitialBoard();
        var intent = new MoveIntent(SideColor.Red, new Position(0, 0), new Position(0, 4)); // Rook over (0,3) Pawn

        var result = _pipeline.Validate(board, intent);

        Assert.False(result.IsValid);
        Assert.Equal(ErrorCodes.PATH_BLOCKED, result.ErrorCode);
    }

    [Fact]
    public void Pipeline_HorseFootBlocked_ShouldReturnHorseFootBlockedError()
    {
        var board = BoardState.CreateInitialBoard();
        var b = board.Pieces.ToBuilder();
        b[new Position(1, 1)] = new PieceState("P", PieceType.Pawn, SideColor.Red, new Position(1, 1));
        var customBoard = new BoardState(b.ToImmutable(), SideColor.Red);

        var intent = new MoveIntent(SideColor.Red, new Position(1, 0), new Position(2, 2));

        var result = _pipeline.Validate(customBoard, intent);

        Assert.False(result.IsValid);
        Assert.Equal(ErrorCodes.HORSE_FOOT_BLOCKED, result.ErrorCode);
    }
}
