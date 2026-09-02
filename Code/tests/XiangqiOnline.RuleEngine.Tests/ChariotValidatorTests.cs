using XiangqiOnline.RuleEngine.Models;
using XiangqiOnline.RuleEngine.Validators;
using XiangqiOnline.Shared.Enums;
using XiangqiOnline.Shared.Models;
using Xunit;

namespace XiangqiOnline.RuleEngine.Tests;

public class ChariotValidatorTests
{
    private readonly ChariotValidator _validator = new();

    [Fact] public void RedChariot_LegalStraightUp_Success() => Assert.True(_validator.Validate(BoardState.CreateInitialBoard(), BoardState.CreateInitialBoard().GetPieceAt(new Position(0, 9))!, new Position(0, 8)).IsValid);
    [Fact] public void RedChariot_PathBlockedByFriendly_Fail() => Assert.Equal(ErrorCodes.PATH_BLOCKED, _validator.Validate(BoardState.CreateInitialBoard(), BoardState.CreateInitialBoard().GetPieceAt(new Position(0, 9))!, new Position(0, 5)).ErrorCode);
    [Fact] public void RedChariot_MoveDiagonal_Fail() => Assert.Equal(ErrorCodes.INVALID_GEOMETRY, _validator.Validate(BoardState.CreateInitialBoard(), BoardState.CreateInitialBoard().GetPieceAt(new Position(0, 9))!, new Position(1, 8)).ErrorCode);
    [Fact]
    public void RedChariot_ZeroDistanceMove_Fail()
    {
        var chariot = new PieceState("R1", PieceType.Chariot, SideColor.Red, new Position(0, 8));
        Assert.Equal(ErrorCodes.INVALID_GEOMETRY, _validator.Validate(BoardState.CreateInitialBoard(), chariot, new Position(0, 8)).ErrorCode);
    }
    [Fact]
    public void RedChariot_HorizontalMove_Success()
    {
        var chariot = new PieceState("R1", PieceType.Chariot, SideColor.Red, new Position(0, 8));
        Assert.True(_validator.Validate(BoardState.CreateInitialBoard(), chariot, new Position(5, 8)).IsValid);
    }
    [Fact]
    public void RedChariot_EatEnemyPiece_Success()
    {
        var chariot = new PieceState("R1", PieceType.Chariot, SideColor.Red, new Position(0, 8));
        var b = BoardState.CreateInitialBoard().Pieces.ToBuilder();
        b.Remove(new Position(0, 6)); // Clear Red Pawn at (0,6)
        b[new Position(0, 3)] = new PieceState("BP", PieceType.Pawn, SideColor.Black, new Position(0, 3));
        Assert.True(_validator.Validate(new BoardState(b.ToImmutable(), SideColor.Red), chariot, new Position(0, 3)).IsValid);
    }
    [Fact]
    public void RedChariot_EatFriendlyPiece_Fail()
    {
        var chariot = new PieceState("R1", PieceType.Chariot, SideColor.Red, new Position(0, 8));
        Assert.Equal(ErrorCodes.ALLY_AT_DESTINATION, _validator.Validate(BoardState.CreateInitialBoard(), chariot, new Position(0, 6)).ErrorCode);
    }
    [Fact] public void BlackChariot_LegalStraightDown_Success() => Assert.True(_validator.Validate(BoardState.CreateInitialBoard(), BoardState.CreateInitialBoard().GetPieceAt(new Position(0, 0))!, new Position(0, 1)).IsValid);
    [Fact]
    public void Chariot_CrossRiver_Success()
    {
        var chariot = new PieceState("R1", PieceType.Chariot, SideColor.Red, new Position(0, 8));
        var b = BoardState.CreateInitialBoard().Pieces.ToBuilder();
        b.Remove(new Position(0, 6)); // Clear Red Pawn at (0,6)
        Assert.True(_validator.Validate(new BoardState(b.ToImmutable(), SideColor.Red), chariot, new Position(0, 4)).IsValid);
    }
    [Fact]
    public void Chariot_HorizontalPathBlocked_Fail()
    {
        var chariot = new PieceState("R1", PieceType.Chariot, SideColor.Red, new Position(0, 8));
        var b = BoardState.CreateInitialBoard().Pieces.ToBuilder();
        b[new Position(3, 8)] = new PieceState("P", PieceType.Pawn, SideColor.Red, new Position(3, 8));
        Assert.Equal(ErrorCodes.PATH_BLOCKED, _validator.Validate(new BoardState(b.ToImmutable(), SideColor.Red), chariot, new Position(5, 8)).ErrorCode);
    }
}
