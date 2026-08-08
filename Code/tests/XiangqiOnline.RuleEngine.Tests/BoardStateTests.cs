using XiangqiOnline.RuleEngine.Models;
using XiangqiOnline.Shared.Enums;
using XiangqiOnline.Shared.Models;
using Xunit;

namespace XiangqiOnline.RuleEngine.Tests;

public class BoardStateTests
{
    [Fact]
    public void CreateInitialBoard_ShouldHaveExact32Pieces_AndUniquePieceIdsAndCoordinates()
    {
        // Act
        var board = BoardState.CreateInitialBoard();

        // Assert
        Assert.NotNull(board);
        Assert.Equal(SideColor.Red, board.Turn);
        var activePieces = board.GetActivePieces().ToList();
        Assert.Equal(32, activePieces.Count);
        Assert.Equal(16, board.GetActivePieces(SideColor.Red).Count());
        Assert.Equal(16, board.GetActivePieces(SideColor.Black).Count());

        // Assert unique PieceIds and unique positions
        var uniqueIds = activePieces.Select(p => p.Id).Distinct().Count();
        Assert.Equal(32, uniqueIds);

        var uniquePositions = activePieces.Select(p => p.Position).Distinct().Count();
        Assert.Equal(32, uniquePositions);
    }

    [Fact]
    public void CreateInitialBoard_ShouldPlaceGeneralsInCorrectPalacePositions()
    {
        // Act
        var board = BoardState.CreateInitialBoard();

        var blackGeneral = board.GetPieceAt(new Position(4, 0));
        var redGeneral = board.GetPieceAt(new Position(4, 9));

        // Assert
        Assert.NotNull(blackGeneral);
        Assert.Equal(PieceType.General, blackGeneral.Type);
        Assert.Equal(SideColor.Black, blackGeneral.Side);
        Assert.Equal("BLACK_GENERAL", blackGeneral.Id);

        Assert.NotNull(redGeneral);
        Assert.Equal(PieceType.General, redGeneral.Type);
        Assert.Equal(SideColor.Red, redGeneral.Side);
        Assert.Equal("RED_GENERAL", redGeneral.Id);
    }

    [Fact]
    public void CreateInitialBoard_ShouldPlaceChariotsAtCorners()
    {
        // Act
        var board = BoardState.CreateInitialBoard();

        // Black Chariots
        var blackChariot1 = board.GetPieceAt(new Position(0, 0));
        var blackChariot2 = board.GetPieceAt(new Position(8, 0));
        Assert.NotNull(blackChariot1);
        Assert.Equal(PieceType.Chariot, blackChariot1.Type);
        Assert.Equal("BLACK_CHARIOT_1", blackChariot1.Id);
        Assert.NotNull(blackChariot2);
        Assert.Equal(PieceType.Chariot, blackChariot2.Type);
        Assert.Equal("BLACK_CHARIOT_2", blackChariot2.Id);

        // Red Chariots
        var redChariot1 = board.GetPieceAt(new Position(0, 9));
        var redChariot2 = board.GetPieceAt(new Position(8, 9));
        Assert.NotNull(redChariot1);
        Assert.Equal(PieceType.Chariot, redChariot1.Type);
        Assert.Equal("RED_CHARIOT_1", redChariot1.Id);
        Assert.NotNull(redChariot2);
        Assert.Equal(PieceType.Chariot, redChariot2.Type);
        Assert.Equal("RED_CHARIOT_2", redChariot2.Id);
    }

    [Fact]
    public void ApplyMove_ShouldReturnNewBoardState_AndKeepOriginalBoardImmutable()
    {
        // Arrange
        var initialBoard = BoardState.CreateInitialBoard();
        var redPawnFrom = new Position(0, 6);
        var redPawnTo = new Position(0, 5);

        // Act
        var newBoard = initialBoard.ApplyMove(redPawnFrom, redPawnTo);

        // Assert - Original board is untouched
        Assert.NotNull(initialBoard.GetPieceAt(redPawnFrom));
        Assert.Null(initialBoard.GetPieceAt(redPawnTo));
        Assert.Equal(SideColor.Red, initialBoard.Turn);

        // Assert - New board updated correctly
        Assert.Null(newBoard.GetPieceAt(redPawnFrom));
        var movedPawn = newBoard.GetPieceAt(redPawnTo);
        Assert.NotNull(movedPawn);
        Assert.Equal(PieceType.Pawn, movedPawn.Type);
        Assert.Equal(SideColor.Black, newBoard.Turn); // Turn switched to Black
    }
}
