using XiangqiOnline.RuleEngine.Models;
using XiangqiOnline.RuleEngine.Validators;
using XiangqiOnline.Shared.Enums;
using XiangqiOnline.Shared.Models;
using Xunit;

namespace XiangqiOnline.RuleEngine.Tests;

public class ElephantValidatorTests
{
    private readonly ElephantValidator _validator = new();

    [Fact] public void RedElephant_LegalMove_Success() => Assert.True(_validator.Validate(BoardState.CreateInitialBoard(), BoardState.CreateInitialBoard().GetPieceAt(new Position(2, 0))!, new Position(4, 2)).IsValid);
    [Fact] public void RedElephant_CrossRiver_Fail() => Assert.Equal(ErrorCodes.CANNOT_CROSS_RIVER, _validator.Validate(BoardState.CreateInitialBoard(), new PieceState("E1", PieceType.Elephant, SideColor.Red, new Position(4, 4)), new Position(2, 6)).ErrorCode);
    [Fact] public void BlackElephant_CrossRiver_Fail() => Assert.Equal(ErrorCodes.CANNOT_CROSS_RIVER, _validator.Validate(BoardState.CreateInitialBoard(), new PieceState("BE1", PieceType.Elephant, SideColor.Black, new Position(4, 5)), new Position(2, 3)).ErrorCode);
    [Fact] public void RedElephant_EyeBlocked_Fail() {
        var board = BoardState.CreateInitialBoard();
        var b = board.Pieces.ToBuilder();
        b[new Position(3, 1)] = new PieceState("P", PieceType.Pawn, SideColor.Red, new Position(3, 1));
        Assert.Equal(ErrorCodes.ELEPHANT_EYE_BLOCKED, _validator.Validate(new BoardState(b.ToImmutable(), SideColor.Red), board.GetPieceAt(new Position(2, 0))!, new Position(4, 2)).ErrorCode);
    }
    [Fact] public void RedElephant_Move1Step_Fail() => Assert.Equal(ErrorCodes.ILLEGAL_PIECE_MOVE, _validator.Validate(BoardState.CreateInitialBoard(), BoardState.CreateInitialBoard().GetPieceAt(new Position(2, 0))!, new Position(3, 1)).ErrorCode);
    [Fact] public void RedElephant_MoveStraight_Fail() => Assert.Equal(ErrorCodes.ILLEGAL_PIECE_MOVE, _validator.Validate(BoardState.CreateInitialBoard(), BoardState.CreateInitialBoard().GetPieceAt(new Position(2, 0))!, new Position(2, 2)).ErrorCode);
    [Fact] public void RedElephant_LandOnFriendlyAdvisor_Fail() {
        var e = new PieceState("E1", PieceType.Elephant, SideColor.Red, new Position(5, 2));
        Assert.Equal(ErrorCodes.DESTINATION_OCCUPIED_BY_FRIEND, _validator.Validate(BoardState.CreateInitialBoard(), e, new Position(3, 0)).ErrorCode);
    }
    [Fact] public void BlackElephant_LegalMove_Success() => Assert.True(_validator.Validate(BoardState.CreateInitialBoard(), BoardState.CreateInitialBoard().GetPieceAt(new Position(2, 9))!, new Position(4, 7)).IsValid);
    [Fact] public void RedElephant_BoundaryTopRiver_Success() => Assert.True(_validator.Validate(BoardState.CreateInitialBoard(), new PieceState("E1", PieceType.Elephant, SideColor.Red, new Position(2, 4)), new Position(4, 2)).IsValid);
    [Fact] public void BlackElephant_BoundaryBottomRiver_Success() => Assert.True(_validator.Validate(BoardState.CreateInitialBoard(), new PieceState("BE1", PieceType.Elephant, SideColor.Black, new Position(2, 5)), new Position(4, 7)).IsValid);
}
