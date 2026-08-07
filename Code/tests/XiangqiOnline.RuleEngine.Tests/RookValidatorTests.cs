using XiangqiOnline.RuleEngine.Models;
using XiangqiOnline.RuleEngine.Validators;
using XiangqiOnline.Shared.Enums;
using XiangqiOnline.Shared.Models;
using Xunit;

namespace XiangqiOnline.RuleEngine.Tests;

public class RookValidatorTests
{
    private readonly RookValidator _validator = new();

    [Fact] public void RedRook_LegalStraightUp_Success() => Assert.True(_validator.Validate(BoardState.CreateInitialBoard(), BoardState.CreateInitialBoard().GetPieceAt(new Position(0, 0))!, new Position(0, 1)).IsValid);
    [Fact] public void RedRook_PathBlockedByFriendly_Fail() => Assert.Equal(ErrorCodes.PATH_BLOCKED, _validator.Validate(BoardState.CreateInitialBoard(), BoardState.CreateInitialBoard().GetPieceAt(new Position(0, 0))!, new Position(0, 4)).ErrorCode);
    [Fact] public void RedRook_MoveDiagonal_Fail() => Assert.Equal(ErrorCodes.ILLEGAL_PIECE_MOVE, _validator.Validate(BoardState.CreateInitialBoard(), BoardState.CreateInitialBoard().GetPieceAt(new Position(0, 0))!, new Position(1, 1)).ErrorCode);
    [Fact] public void RedRook_ZeroDistanceMove_Fail() {
        var rook = new PieceState("R1", PieceType.Rook, SideColor.Red, new Position(0, 1));
        Assert.Equal(ErrorCodes.ILLEGAL_PIECE_MOVE, _validator.Validate(BoardState.CreateInitialBoard(), rook, new Position(0, 1)).ErrorCode);
    }
    [Fact] public void RedRook_HorizontalMove_Success() {
        var rook = new PieceState("R1", PieceType.Rook, SideColor.Red, new Position(0, 1));
        Assert.True(_validator.Validate(BoardState.CreateInitialBoard(), rook, new Position(5, 1)).IsValid);
    }
    [Fact] public void RedRook_EatEnemyPiece_Success() {
        var rook = new PieceState("R1", PieceType.Rook, SideColor.Red, new Position(0, 1));
        var b = BoardState.CreateInitialBoard().Pieces.ToBuilder();
        b.Remove(new Position(0, 3)); // Clear Red Pawn at (0,3)
        b[new Position(0, 6)] = new PieceState("BP", PieceType.Pawn, SideColor.Black, new Position(0, 6));
        Assert.True(_validator.Validate(new BoardState(b.ToImmutable(), SideColor.Red), rook, new Position(0, 6)).IsValid);
    }
    [Fact] public void RedRook_EatFriendlyPiece_Fail() {
        var rook = new PieceState("R1", PieceType.Rook, SideColor.Red, new Position(0, 1));
        Assert.Equal(ErrorCodes.DESTINATION_OCCUPIED_BY_FRIEND, _validator.Validate(BoardState.CreateInitialBoard(), rook, new Position(0, 3)).ErrorCode);
    }
    [Fact] public void BlackRook_LegalStraightDown_Success() => Assert.True(_validator.Validate(BoardState.CreateInitialBoard(), BoardState.CreateInitialBoard().GetPieceAt(new Position(0, 9))!, new Position(0, 8)).IsValid);
    [Fact] public void Rook_CrossRiver_Success() {
        var rook = new PieceState("R1", PieceType.Rook, SideColor.Red, new Position(0, 1));
        var b = BoardState.CreateInitialBoard().Pieces.ToBuilder();
        b.Remove(new Position(0, 3)); // Clear Red Pawn at (0,3)
        Assert.True(_validator.Validate(new BoardState(b.ToImmutable(), SideColor.Red), rook, new Position(0, 5)).IsValid);
    }
    [Fact] public void Rook_HorizontalPathBlocked_Fail() {
        var rook = new PieceState("R1", PieceType.Rook, SideColor.Red, new Position(0, 1));
        var b = BoardState.CreateInitialBoard().Pieces.ToBuilder();
        b[new Position(3, 1)] = new PieceState("P", PieceType.Pawn, SideColor.Red, new Position(3, 1));
        Assert.Equal(ErrorCodes.PATH_BLOCKED, _validator.Validate(new BoardState(b.ToImmutable(), SideColor.Red), rook, new Position(5, 1)).ErrorCode);
    }
}
