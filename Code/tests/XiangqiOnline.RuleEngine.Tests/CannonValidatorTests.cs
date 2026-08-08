using XiangqiOnline.RuleEngine.Models;
using XiangqiOnline.RuleEngine.Validators;
using XiangqiOnline.Shared.Enums;
using XiangqiOnline.Shared.Models;
using Xunit;

namespace XiangqiOnline.RuleEngine.Tests;

public class CannonValidatorTests
{
    private readonly CannonValidator _validator = new();

    [Fact] public void Cannon_LegalMoveNoEating_Success() => Assert.True(_validator.Validate(BoardState.CreateInitialBoard(), BoardState.CreateInitialBoard().GetPieceAt(new Position(1, 7))!, new Position(4, 7)).IsValid);
    [Fact] public void Cannon_MoveNoEatingWithMount_Fail() {
        var board = BoardState.CreateInitialBoard();
        var b = board.Pieces.ToBuilder();
        b[new Position(1, 6)] = new PieceState("M", PieceType.Pawn, SideColor.Red, new Position(1, 6));
        Assert.Equal(ErrorCodes.PATH_BLOCKED, _validator.Validate(new BoardState(b.ToImmutable(), SideColor.Red), board.GetPieceAt(new Position(1, 7))!, new Position(1, 4)).ErrorCode);
    }
    [Fact] public void Cannon_EatWithExact1Mount_Success() {
        var board = BoardState.CreateInitialBoard();
        var b = board.Pieces.ToBuilder();
        b[new Position(1, 5)] = new PieceState("M", PieceType.Pawn, SideColor.Red, new Position(1, 5));
        b[new Position(1, 3)] = new PieceState("BP", PieceType.Pawn, SideColor.Black, new Position(1, 3));
        Assert.True(_validator.Validate(new BoardState(b.ToImmutable(), SideColor.Red), board.GetPieceAt(new Position(1, 7))!, new Position(1, 3)).IsValid);
    }
    [Fact] public void Cannon_EatWith0Mounts_Fail() {
        var board = BoardState.CreateInitialBoard();
        var b = board.Pieces.ToBuilder();
        b[new Position(4, 7)] = new PieceState("BP", PieceType.Pawn, SideColor.Black, new Position(4, 7));
        Assert.Equal(ErrorCodes.CANNON_SCREEN_INVALID, _validator.Validate(new BoardState(b.ToImmutable(), SideColor.Red), board.GetPieceAt(new Position(1, 7))!, new Position(4, 7)).ErrorCode);
    }
    [Fact] public void Cannon_EatWith2Mounts_Fail() {
        var board = BoardState.CreateInitialBoard();
        var b = board.Pieces.ToBuilder();
        b[new Position(2, 7)] = new PieceState("M1", PieceType.Pawn, SideColor.Red, new Position(2, 7));
        b[new Position(3, 7)] = new PieceState("M2", PieceType.Pawn, SideColor.Red, new Position(3, 7));
        b[new Position(5, 7)] = new PieceState("BP", PieceType.Pawn, SideColor.Black, new Position(5, 7));
        Assert.Equal(ErrorCodes.CANNON_SCREEN_INVALID, _validator.Validate(new BoardState(b.ToImmutable(), SideColor.Red), board.GetPieceAt(new Position(1, 7))!, new Position(5, 7)).ErrorCode);
    }
    [Fact] public void Cannon_DiagonalMove_Fail() => Assert.Equal(ErrorCodes.INVALID_GEOMETRY, _validator.Validate(BoardState.CreateInitialBoard(), BoardState.CreateInitialBoard().GetPieceAt(new Position(1, 7))!, new Position(2, 6)).ErrorCode);
    [Fact] public void Cannon_EatFriendlyPieceWith1Mount_Fail() {
        var board = BoardState.CreateInitialBoard();
        var b = board.Pieces.ToBuilder();
        b[new Position(1, 5)] = new PieceState("M", PieceType.Pawn, SideColor.Black, new Position(1, 5));
        b[new Position(1, 3)] = new PieceState("RP", PieceType.Pawn, SideColor.Red, new Position(1, 3));
        Assert.Equal(ErrorCodes.ALLY_AT_DESTINATION, _validator.Validate(new BoardState(b.ToImmutable(), SideColor.Red), board.GetPieceAt(new Position(1, 7))!, new Position(1, 3)).ErrorCode);
    }
    [Fact] public void BlackCannon_LegalMove_Success() => Assert.True(_validator.Validate(BoardState.CreateInitialBoard(), BoardState.CreateInitialBoard().GetPieceAt(new Position(1, 2))!, new Position(4, 2)).IsValid);
    [Fact] public void Cannon_HorizontalEatWith1Mount_Success() {
        var cannon = new PieceState("C1", PieceType.Cannon, SideColor.Red, new Position(0, 4));
        var b = BoardState.CreateInitialBoard().Pieces.ToBuilder();
        b[new Position(3, 4)] = new PieceState("M", PieceType.Pawn, SideColor.Red, new Position(3, 4));
        b[new Position(6, 4)] = new PieceState("BP", PieceType.Pawn, SideColor.Black, new Position(6, 4));
        Assert.True(_validator.Validate(new BoardState(b.ToImmutable(), SideColor.Red), cannon, new Position(6, 4)).IsValid);
    }
    [Fact] public void Cannon_SamePosition_Fail() => Assert.Equal(ErrorCodes.INVALID_GEOMETRY, _validator.Validate(BoardState.CreateInitialBoard(), BoardState.CreateInitialBoard().GetPieceAt(new Position(1, 7))!, new Position(1, 7)).ErrorCode);
    [Fact] public void Cannon_EatEnemyChariotWith1Mount_Success() {
        var board = BoardState.CreateInitialBoard();
        var b = board.Pieces.ToBuilder();
        b.Remove(new Position(7, 3)); // Clear Black Pawn at (7,3)
        b.Remove(new Position(7, 2)); // Clear Black Cannon at (7,2)
        b[new Position(7, 5)] = new PieceState("M", PieceType.Pawn, SideColor.Red, new Position(7, 5));
        Assert.True(_validator.Validate(new BoardState(b.ToImmutable(), SideColor.Red), board.GetPieceAt(new Position(7, 7))!, new Position(7, 0)).IsValid);
    }
    [Fact] public void Cannon_BackwardMoveNoMount_Success() {
        var cannon = new PieceState("C1", PieceType.Cannon, SideColor.Red, new Position(1, 4));
        Assert.True(_validator.Validate(BoardState.CreateInitialBoard(), cannon, new Position(1, 5)).IsValid);
    }
    [Fact] public void Cannon_OverRiverMove_Success() {
        var cannon = new PieceState("C1", PieceType.Cannon, SideColor.Red, new Position(1, 7));
        Assert.True(_validator.Validate(BoardState.CreateInitialBoard(), cannon, new Position(1, 4)).IsValid);
    }
    [Fact] public void BlackCannon_EatRedPawn_Success() {
        var blackCannon = new PieceState("BLACK_CANNON_1", PieceType.Cannon, SideColor.Black, new Position(1, 2));
        var screenPiece = new PieceState("BLACK_PAWN_1", PieceType.Pawn, SideColor.Black, new Position(1, 4));
        var redPawnTarget = new PieceState("RED_PAWN_1", PieceType.Pawn, SideColor.Red, new Position(1, 6));
        var customBoard = Fixtures.BoardSetupFixture.CreateBoardWithPieces(SideColor.Black, blackCannon, screenPiece, redPawnTarget);

        var result = _validator.Validate(customBoard, blackCannon, new Position(1, 6));

        Assert.True(result.IsValid);
        Assert.Equal(ErrorCodes.OK, result.ErrorCode);
    }
}
