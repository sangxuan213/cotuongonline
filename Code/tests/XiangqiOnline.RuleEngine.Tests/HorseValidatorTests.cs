using XiangqiOnline.RuleEngine.Models;
using XiangqiOnline.RuleEngine.Validators;
using XiangqiOnline.Shared.Enums;
using XiangqiOnline.Shared.Models;
using Xunit;

namespace XiangqiOnline.RuleEngine.Tests;

public class HorseValidatorTests
{
    private readonly HorseValidator _validator = new();

    [Fact] public void RedHorse_LegalUpRight_Success() => Assert.True(_validator.Validate(BoardState.CreateInitialBoard(), BoardState.CreateInitialBoard().GetPieceAt(new Position(1, 9))!, new Position(2, 7)).IsValid);
    [Fact] public void RedHorse_LegalUpLeft_Success() => Assert.True(_validator.Validate(BoardState.CreateInitialBoard(), BoardState.CreateInitialBoard().GetPieceAt(new Position(1, 9))!, new Position(0, 7)).IsValid);
    [Fact] public void RedHorse_FootBlockedUp_Fail() {
        var board = BoardState.CreateInitialBoard();
        var b = board.Pieces.ToBuilder();
        b[new Position(1, 8)] = new PieceState("P", PieceType.Pawn, SideColor.Red, new Position(1, 8));
        Assert.Equal(ErrorCodes.HORSE_LEG_BLOCKED, _validator.Validate(new BoardState(b.ToImmutable(), SideColor.Red), board.GetPieceAt(new Position(1, 9))!, new Position(2, 7)).ErrorCode);
    }
    [Fact] public void RedHorse_FootBlockedRight_Fail() {
        var horse = new PieceState("H1", PieceType.Horse, SideColor.Red, new Position(4, 4));
        var board = BoardState.CreateInitialBoard();
        var b = board.Pieces.ToBuilder();
        b[new Position(5, 4)] = new PieceState("P", PieceType.Pawn, SideColor.Red, new Position(5, 4));
        Assert.Equal(ErrorCodes.HORSE_LEG_BLOCKED, _validator.Validate(new BoardState(b.ToImmutable(), SideColor.Red), horse, new Position(6, 5)).ErrorCode);
    }
    [Fact] public void RedHorse_MoveStraight_Fail() => Assert.Equal(ErrorCodes.INVALID_GEOMETRY, _validator.Validate(BoardState.CreateInitialBoard(), BoardState.CreateInitialBoard().GetPieceAt(new Position(1, 9))!, new Position(1, 6)).ErrorCode);
    [Fact] public void RedHorse_MoveDiagonal2x2_Fail() => Assert.Equal(ErrorCodes.INVALID_GEOMETRY, _validator.Validate(BoardState.CreateInitialBoard(), BoardState.CreateInitialBoard().GetPieceAt(new Position(1, 9))!, new Position(3, 7)).ErrorCode);
    [Fact] public void RedHorse_LandOnFriendlyPiece_Fail() {
        var horse = new PieceState("H1", PieceType.Horse, SideColor.Red, new Position(3, 7));
        Assert.Equal(ErrorCodes.ALLY_AT_DESTINATION, _validator.Validate(BoardState.CreateInitialBoard(), horse, new Position(2, 9)).ErrorCode);
    }
    [Fact] public void BlackHorse_LegalMove_Success() => Assert.True(_validator.Validate(BoardState.CreateInitialBoard(), BoardState.CreateInitialBoard().GetPieceAt(new Position(1, 0))!, new Position(2, 2)).IsValid);
    [Fact] public void Horse_HorizontalLMove_Success() {
        var horse = new PieceState("H1", PieceType.Horse, SideColor.Red, new Position(4, 4));
        Assert.True(_validator.Validate(BoardState.CreateInitialBoard(), horse, new Position(6, 5)).IsValid);
    }
    [Fact] public void Horse_HorizontalLMove_FootBlock_Fail() {
        var horse = new PieceState("H1", PieceType.Horse, SideColor.Red, new Position(4, 4));
        var b = BoardState.CreateInitialBoard().Pieces.ToBuilder();
        b[new Position(5, 4)] = new PieceState("P", PieceType.Pawn, SideColor.Black, new Position(5, 4));
        Assert.Equal(ErrorCodes.HORSE_LEG_BLOCKED, _validator.Validate(new BoardState(b.ToImmutable(), SideColor.Red), horse, new Position(6, 5)).ErrorCode);
    }
    [Fact] public void Horse_CrossRiver_Success() {
        var horse = new PieceState("H1", PieceType.Horse, SideColor.Red, new Position(4, 4));
        Assert.True(_validator.Validate(BoardState.CreateInitialBoard(), horse, new Position(5, 6)).IsValid);
    }
    [Fact] public void Horse_CornerBoundaryMove_Success() {
        var horse = new PieceState("H1", PieceType.Horse, SideColor.Red, new Position(0, 4));
        Assert.True(_validator.Validate(BoardState.CreateInitialBoard(), horse, new Position(1, 6)).IsValid);
    }
}
