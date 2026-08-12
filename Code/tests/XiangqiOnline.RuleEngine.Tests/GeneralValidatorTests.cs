using XiangqiOnline.RuleEngine.Models;
using XiangqiOnline.RuleEngine.Validators;
using XiangqiOnline.Shared.Enums;
using XiangqiOnline.Shared.Models;
using Xunit;

namespace XiangqiOnline.RuleEngine.Tests;

public class GeneralValidatorTests
{
    private readonly GeneralValidator _validator = new();

    [Fact] public void RedGeneral_LegalUp_Success() => Assert.True(_validator.Validate(BoardState.CreateInitialBoard(), BoardState.CreateInitialBoard().GetPieceAt(new Position(4, 9))!, new Position(4, 8)).IsValid);
    [Fact] public void RedGeneral_MoveLeftOutOfPalace_Fail() => Assert.Equal(ErrorCodes.OUTSIDE_PALACE, _validator.Validate(BoardState.CreateInitialBoard(), new PieceState("RED_GENERAL", PieceType.General, SideColor.Red, new Position(3, 9)), new Position(2, 9)).ErrorCode);
    [Fact] public void RedGeneral_MoveRightOutOfPalace_Fail() => Assert.Equal(ErrorCodes.OUTSIDE_PALACE, _validator.Validate(BoardState.CreateInitialBoard(), new PieceState("RED_GENERAL", PieceType.General, SideColor.Red, new Position(5, 9)), new Position(6, 9)).ErrorCode);
    [Fact] public void RedGeneral_MoveUpOutOfPalace_Fail() => Assert.Equal(ErrorCodes.OUTSIDE_PALACE, _validator.Validate(BoardState.CreateInitialBoard(), new PieceState("RED_GENERAL", PieceType.General, SideColor.Red, new Position(4, 7)), new Position(4, 6)).ErrorCode);
    [Fact] public void RedGeneral_MoveDiagonal_Fail() => Assert.Equal(ErrorCodes.INVALID_GEOMETRY, _validator.Validate(BoardState.CreateInitialBoard(), BoardState.CreateInitialBoard().GetPieceAt(new Position(4, 9))!, new Position(5, 8)).ErrorCode);
    [Fact] public void RedGeneral_Move2Steps_Fail() => Assert.Equal(ErrorCodes.INVALID_GEOMETRY, _validator.Validate(BoardState.CreateInitialBoard(), BoardState.CreateInitialBoard().GetPieceAt(new Position(4, 9))!, new Position(4, 7)).ErrorCode);
    [Fact] public void RedGeneral_LandOnFriendlyAdvisor_Fail() => Assert.Equal(ErrorCodes.ALLY_AT_DESTINATION, _validator.Validate(BoardState.CreateInitialBoard(), BoardState.CreateInitialBoard().GetPieceAt(new Position(4, 9))!, new Position(3, 9)).ErrorCode);
    [Fact] public void BlackGeneral_LegalMoveInsidePalace_Success() => Assert.True(_validator.Validate(BoardState.CreateInitialBoard(), BoardState.CreateInitialBoard().GetPieceAt(new Position(4, 0))!, new Position(4, 1)).IsValid);
}
