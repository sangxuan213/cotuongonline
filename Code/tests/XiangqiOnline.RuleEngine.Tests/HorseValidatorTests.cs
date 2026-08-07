using XiangqiOnline.RuleEngine.Models;
using XiangqiOnline.RuleEngine.Validators;
using XiangqiOnline.Shared.Enums;
using XiangqiOnline.Shared.Models;
using Xunit;

namespace XiangqiOnline.RuleEngine.Tests;

public class HorseValidatorTests
{
    private readonly HorseValidator _validator = new();

    [Fact] public void RedHorse_LegalUpRight_Success() => Assert.True(_validator.Validate(BoardState.CreateInitialBoard(), BoardState.CreateInitialBoard().GetPieceAt(new Position(1, 0))!, new Position(2, 2)).IsValid);
    [Fact] public void RedHorse_LegalUpLeft_Success() => Assert.True(_validator.Validate(BoardState.CreateInitialBoard(), BoardState.CreateInitialBoard().GetPieceAt(new Position(1, 0))!, new Position(0, 2)).IsValid);
    [Fact] public void RedHorse_FootBlockedUp_Fail() {
        var board = BoardState.CreateInitialBoard();
        var b = board.Pieces.ToBuilder();
        b[new Position(1, 1)] = new PieceState("P", PieceType.Pawn, SideColor.Red, new Position(1, 1));
        Assert.Equal(ErrorCodes.HORSE_FOOT_BLOCKED, _validator.Validate(new BoardState(b.ToImmutable(), SideColor.Red), board.GetPieceAt(new Position(1, 0))!, new Position(2, 2)).ErrorCode);
    }
    [Fact] public void RedHorse_FootBlockedRight_Fail() {
        var horse = new PieceState("H1", PieceType.Horse, SideColor.Red, new Position(4, 4));
        var board = BoardState.CreateInitialBoard();
        var b = board.Pieces.ToBuilder();
        b[new Position(5, 4)] = new PieceState("P", PieceType.Pawn, SideColor.Red, new Position(5, 4));
        Assert.Equal(ErrorCodes.HORSE_FOOT_BLOCKED, _validator.Validate(new BoardState(b.ToImmutable(), SideColor.Red), horse, new Position(6, 5)).ErrorCode);
    }
    [Fact] public void RedHorse_MoveStraight_Fail() => Assert.Equal(ErrorCodes.ILLEGAL_PIECE_MOVE, _validator.Validate(BoardState.CreateInitialBoard(), BoardState.CreateInitialBoard().GetPieceAt(new Position(1, 0))!, new Position(1, 3)).ErrorCode);
    [Fact] public void RedHorse_MoveDiagonal2x2_Fail() => Assert.Equal(ErrorCodes.ILLEGAL_PIECE_MOVE, _validator.Validate(BoardState.CreateInitialBoard(), BoardState.CreateInitialBoard().GetPieceAt(new Position(1, 0))!, new Position(3, 2)).ErrorCode);
    [Fact] public void RedHorse_LandOnFriendlyPiece_Fail() {
        var horse = new PieceState("H1", PieceType.Horse, SideColor.Red, new Position(3, 2));
        Assert.Equal(ErrorCodes.DESTINATION_OCCUPIED_BY_FRIEND, _validator.Validate(BoardState.CreateInitialBoard(), horse, new Position(2, 0)).ErrorCode);
    }
    [Fact] public void BlackHorse_LegalMove_Success() => Assert.True(_validator.Validate(BoardState.CreateInitialBoard(), BoardState.CreateInitialBoard().GetPieceAt(new Position(1, 9))!, new Position(2, 7)).IsValid);
    [Fact] public void Horse_HorizontalLMove_Success() {
        var horse = new PieceState("H1", PieceType.Horse, SideColor.Red, new Position(4, 4));
        Assert.True(_validator.Validate(BoardState.CreateInitialBoard(), horse, new Position(6, 5)).IsValid);
    }
    [Fact] public void Horse_HorizontalLMove_FootBlock_Fail() {
        var horse = new PieceState("H1", PieceType.Horse, SideColor.Red, new Position(4, 4));
        var b = BoardState.CreateInitialBoard().Pieces.ToBuilder();
        b[new Position(5, 4)] = new PieceState("P", PieceType.Pawn, SideColor.Black, new Position(5, 4));
        Assert.Equal(ErrorCodes.HORSE_FOOT_BLOCKED, _validator.Validate(new BoardState(b.ToImmutable(), SideColor.Red), horse, new Position(6, 5)).ErrorCode);
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
