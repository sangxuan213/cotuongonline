using XiangqiOnline.RuleEngine.Models;
using XiangqiOnline.RuleEngine.Validators;
using XiangqiOnline.Shared.Enums;
using XiangqiOnline.Shared.Models;
using Xunit;

namespace XiangqiOnline.RuleEngine.Tests;

public class ElephantValidatorTests
{
    private readonly ElephantValidator _validator = new();

    [Fact] public void RedElephant_LegalMove_Success() => Assert.True(_validator.Validate(BoardState.CreateInitialBoard(), BoardState.CreateInitialBoard().GetPieceAt(new Position(2, 9))!, new Position(4, 7)).IsValid);
    [Fact] public void RedElephant_CrossRiver_Fail() => Assert.Equal(ErrorCodes.ELEPHANT_CROSSES_RIVER, _validator.Validate(BoardState.CreateInitialBoard(), new PieceState("E1", PieceType.Elephant, SideColor.Red, new Position(4, 5)), new Position(2, 3)).ErrorCode);
    [Fact] public void BlackElephant_CrossRiver_Fail() => Assert.Equal(ErrorCodes.ELEPHANT_CROSSES_RIVER, _validator.Validate(BoardState.CreateInitialBoard(), new PieceState("BE1", PieceType.Elephant, SideColor.Black, new Position(4, 4)), new Position(2, 6)).ErrorCode);
    [Fact]
    public void RedElephant_EyeBlocked_Fail()
    {
        var board = BoardState.CreateInitialBoard();
        var b = board.Pieces.ToBuilder();
        b[new Position(3, 8)] = new PieceState("P", PieceType.Pawn, SideColor.Red, new Position(3, 8));
        Assert.Equal(ErrorCodes.ELEPHANT_EYE_BLOCKED, _validator.Validate(new BoardState(b.ToImmutable(), SideColor.Red), board.GetPieceAt(new Position(2, 9))!, new Position(4, 7)).ErrorCode);
    }
    [Fact] public void RedElephant_Move1Step_Fail() => Assert.Equal(ErrorCodes.INVALID_GEOMETRY, _validator.Validate(BoardState.CreateInitialBoard(), BoardState.CreateInitialBoard().GetPieceAt(new Position(2, 9))!, new Position(3, 8)).ErrorCode);
    [Fact] public void RedElephant_MoveStraight_Fail() => Assert.Equal(ErrorCodes.INVALID_GEOMETRY, _validator.Validate(BoardState.CreateInitialBoard(), BoardState.CreateInitialBoard().GetPieceAt(new Position(2, 9))!, new Position(2, 7)).ErrorCode);
    [Fact]
    public void RedElephant_LandOnFriendlyAdvisor_Fail()
    {
        var e = new PieceState("E1", PieceType.Elephant, SideColor.Red, new Position(5, 7));
        Assert.Equal(ErrorCodes.ALLY_AT_DESTINATION, _validator.Validate(BoardState.CreateInitialBoard(), e, new Position(3, 9)).ErrorCode);
    }
    [Fact] public void BlackElephant_LegalMove_Success() => Assert.True(_validator.Validate(BoardState.CreateInitialBoard(), BoardState.CreateInitialBoard().GetPieceAt(new Position(2, 0))!, new Position(4, 2)).IsValid);
    [Fact] public void RedElephant_BoundaryTopRiver_Success() => Assert.True(_validator.Validate(BoardState.CreateInitialBoard(), new PieceState("E1", PieceType.Elephant, SideColor.Red, new Position(2, 5)), new Position(4, 7)).IsValid);
    [Fact] public void BlackElephant_BoundaryBottomRiver_Success() => Assert.True(_validator.Validate(BoardState.CreateInitialBoard(), new PieceState("BE1", PieceType.Elephant, SideColor.Black, new Position(2, 4)), new Position(4, 2)).IsValid);
}
