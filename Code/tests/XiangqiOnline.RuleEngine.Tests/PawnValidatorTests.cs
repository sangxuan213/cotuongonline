using XiangqiOnline.RuleEngine.Models;
using XiangqiOnline.RuleEngine.Validators;
using XiangqiOnline.Shared.Enums;
using XiangqiOnline.Shared.Models;
using Xunit;

namespace XiangqiOnline.RuleEngine.Tests;

public class PawnValidatorTests
{
    private readonly PawnValidator _validator = new();

    [Fact] public void RedPawn_ForwardBeforeRiver_Success() => Assert.True(_validator.Validate(BoardState.CreateInitialBoard(), BoardState.CreateInitialBoard().GetPieceAt(new Position(0, 6))!, new Position(0, 5)).IsValid);
    [Fact] public void RedPawn_SidewaysBeforeRiver_Fail() => Assert.Equal(ErrorCodes.INVALID_GEOMETRY, _validator.Validate(BoardState.CreateInitialBoard(), BoardState.CreateInitialBoard().GetPieceAt(new Position(0, 6))!, new Position(1, 6)).ErrorCode);
    [Fact] public void RedPawn_Backward_Fail() => Assert.Equal(ErrorCodes.PAWN_RETREATS, _validator.Validate(BoardState.CreateInitialBoard(), BoardState.CreateInitialBoard().GetPieceAt(new Position(0, 6))!, new Position(0, 7)).ErrorCode);
    [Fact] public void RedPawn_SidewaysAfterRiver_Success() => Assert.True(_validator.Validate(BoardState.CreateInitialBoard(), new PieceState("RP", PieceType.Pawn, SideColor.Red, new Position(4, 4)), new Position(5, 4)).IsValid);
    [Fact] public void RedPawn_ForwardAfterRiver_Success() => Assert.True(_validator.Validate(BoardState.CreateInitialBoard(), new PieceState("RP", PieceType.Pawn, SideColor.Red, new Position(4, 4)), new Position(4, 3)).IsValid);
    [Fact] public void BlackPawn_ForwardBeforeRiver_Success() => Assert.True(_validator.Validate(BoardState.CreateInitialBoard(), BoardState.CreateInitialBoard().GetPieceAt(new Position(0, 3))!, new Position(0, 4)).IsValid);
    [Fact] public void BlackPawn_Backward_Fail() => Assert.Equal(ErrorCodes.PAWN_RETREATS, _validator.Validate(BoardState.CreateInitialBoard(), BoardState.CreateInitialBoard().GetPieceAt(new Position(0, 3))!, new Position(0, 2)).ErrorCode);
    [Fact] public void BlackPawn_SidewaysAfterRiver_Success() => Assert.True(_validator.Validate(BoardState.CreateInitialBoard(), new PieceState("BP", PieceType.Pawn, SideColor.Black, new Position(4, 5)), new Position(3, 5)).IsValid);
    [Fact] public void RedPawn_Move2Steps_Fail() => Assert.Equal(ErrorCodes.INVALID_GEOMETRY, _validator.Validate(BoardState.CreateInitialBoard(), BoardState.CreateInitialBoard().GetPieceAt(new Position(0, 6))!, new Position(0, 4)).ErrorCode);
    [Fact] public void RedPawn_DiagonalMove_Fail() => Assert.Equal(ErrorCodes.INVALID_GEOMETRY, _validator.Validate(BoardState.CreateInitialBoard(), BoardState.CreateInitialBoard().GetPieceAt(new Position(0, 6))!, new Position(1, 5)).ErrorCode);
}
