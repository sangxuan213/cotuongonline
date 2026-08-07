using XiangqiOnline.RuleEngine.Models;
using XiangqiOnline.RuleEngine.Validators;
using XiangqiOnline.Shared.Enums;
using XiangqiOnline.Shared.Models;
using Xunit;

namespace XiangqiOnline.RuleEngine.Tests;

public class AdvisorValidatorTests
{
    private readonly AdvisorValidator _validator = new();

    [Fact] public void RedAdvisor_LegalDiagonalUp_Success() => Assert.True(_validator.Validate(BoardState.CreateInitialBoard(), BoardState.CreateInitialBoard().GetPieceAt(new Position(3, 0))!, new Position(4, 1)).IsValid);
    [Fact] public void RedAdvisor_MoveStraight_Fail() => Assert.Equal(ErrorCodes.ILLEGAL_PIECE_MOVE, _validator.Validate(BoardState.CreateInitialBoard(), BoardState.CreateInitialBoard().GetPieceAt(new Position(3, 0))!, new Position(3, 1)).ErrorCode);
    [Fact] public void RedAdvisor_MoveOutOfPalace_Fail() => Assert.Equal(ErrorCodes.OUT_OF_PALACE, _validator.Validate(BoardState.CreateInitialBoard(), new PieceState("A1", PieceType.Advisor, SideColor.Red, new Position(3, 2)), new Position(2, 3)).ErrorCode);
    [Fact] public void RedAdvisor_Move2StepsDiagonal_Fail() => Assert.Equal(ErrorCodes.ILLEGAL_PIECE_MOVE, _validator.Validate(BoardState.CreateInitialBoard(), BoardState.CreateInitialBoard().GetPieceAt(new Position(3, 0))!, new Position(5, 2)).ErrorCode);
    [Fact] public void BlackAdvisor_LegalDiagonalDown_Success() => Assert.True(_validator.Validate(BoardState.CreateInitialBoard(), BoardState.CreateInitialBoard().GetPieceAt(new Position(3, 9))!, new Position(4, 8)).IsValid);
    [Fact] public void RedAdvisor_LandOnFriendlyGeneral_Fail() => Assert.Equal(ErrorCodes.DESTINATION_OCCUPIED_BY_FRIEND, _validator.Validate(BoardState.CreateInitialBoard(), new PieceState("A1", PieceType.Advisor, SideColor.Red, new Position(5, 1)), new Position(4, 0)).ErrorCode);
}
