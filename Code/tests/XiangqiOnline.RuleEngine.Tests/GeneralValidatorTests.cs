using XiangqiOnline.RuleEngine.Models;
using XiangqiOnline.RuleEngine.Validators;
using XiangqiOnline.Shared.Enums;
using XiangqiOnline.Shared.Models;
using Xunit;

namespace XiangqiOnline.RuleEngine.Tests;

public class GeneralValidatorTests
{
    private readonly GeneralValidator _validator = new();

    [Fact] public void RedGeneral_LegalUp_Success() => Assert.True(_validator.Validate(BoardState.CreateInitialBoard(), BoardState.CreateInitialBoard().GetPieceAt(new Position(4, 0))!, new Position(4, 1)).IsValid);
    [Fact] public void RedGeneral_MoveLeftOutOfPalace_Fail() => Assert.Equal(ErrorCodes.OUT_OF_PALACE, _validator.Validate(BoardState.CreateInitialBoard(), new PieceState("R_G", PieceType.General, SideColor.Red, new Position(3, 0)), new Position(2, 0)).ErrorCode);
    [Fact] public void RedGeneral_MoveRightOutOfPalace_Fail() => Assert.Equal(ErrorCodes.OUT_OF_PALACE, _validator.Validate(BoardState.CreateInitialBoard(), new PieceState("R_G", PieceType.General, SideColor.Red, new Position(5, 0)), new Position(6, 0)).ErrorCode);
    [Fact] public void RedGeneral_MoveUpOutOfPalace_Fail() => Assert.Equal(ErrorCodes.OUT_OF_PALACE, _validator.Validate(BoardState.CreateInitialBoard(), new PieceState("R_G", PieceType.General, SideColor.Red, new Position(4, 2)), new Position(4, 3)).ErrorCode);
    [Fact] public void RedGeneral_MoveDiagonal_Fail() => Assert.Equal(ErrorCodes.ILLEGAL_PIECE_MOVE, _validator.Validate(BoardState.CreateInitialBoard(), BoardState.CreateInitialBoard().GetPieceAt(new Position(4, 0))!, new Position(5, 1)).ErrorCode);
    [Fact] public void RedGeneral_Move2Steps_Fail() => Assert.Equal(ErrorCodes.ILLEGAL_PIECE_MOVE, _validator.Validate(BoardState.CreateInitialBoard(), BoardState.CreateInitialBoard().GetPieceAt(new Position(4, 0))!, new Position(4, 2)).ErrorCode);
    [Fact] public void RedGeneral_LandOnFriendlyAdvisor_Fail() => Assert.Equal(ErrorCodes.DESTINATION_OCCUPIED_BY_FRIEND, _validator.Validate(BoardState.CreateInitialBoard(), BoardState.CreateInitialBoard().GetPieceAt(new Position(4, 0))!, new Position(3, 0)).ErrorCode);
    [Fact] public void BlackGeneral_LegalMoveInsidePalace_Success() => Assert.True(_validator.Validate(BoardState.CreateInitialBoard(), BoardState.CreateInitialBoard().GetPieceAt(new Position(4, 9))!, new Position(4, 8)).IsValid);
}
