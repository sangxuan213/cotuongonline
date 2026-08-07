using XiangqiOnline.RuleEngine.Models;
using XiangqiOnline.Shared.Enums;
using XiangqiOnline.Shared.Models;
using Xunit;

namespace XiangqiOnline.RuleEngine.Tests;

public class BoardStateTests
{
    [Fact]
    public void CreateInitialBoard_ShouldHaveExact32Pieces()
    {
        // Act
        var board = BoardState.CreateInitialBoard();

        // Assert
        Assert.NotNull(board);
        Assert.Equal(SideColor.Red, board.Turn);
        Assert.Equal(32, board.GetActivePieces().Count());
        Assert.Equal(16, board.GetActivePieces(SideColor.Red).Count());
        Assert.Equal(16, board.GetActivePieces(SideColor.Black).Count());
    }

    [Fact]
    public void CreateInitialBoard_ShouldPlaceGeneralsInCorrectPalacePositions()
    {
        // Act
        var board = BoardState.CreateInitialBoard();

        var redGeneral = board.GetPieceAt(new Position(4, 0));
        var blackGeneral = board.GetPieceAt(new Position(4, 9));

        // Assert
        Assert.NotNull(redGeneral);
        Assert.Equal(PieceType.General, redGeneral.Type);
        Assert.Equal(SideColor.Red, redGeneral.Side);

        Assert.NotNull(blackGeneral);
        Assert.Equal(PieceType.General, blackGeneral.Type);
        Assert.Equal(SideColor.Black, blackGeneral.Side);
    }

    [Fact]
    public void CreateInitialBoard_ShouldPlaceRooksAtCorners()
    {
        // Act
        var board = BoardState.CreateInitialBoard();

        // Red Rooks
        var redRook1 = board.GetPieceAt(new Position(0, 0));
        var redRook2 = board.GetPieceAt(new Position(8, 0));
        Assert.NotNull(redRook1);
        Assert.Equal(PieceType.Rook, redRook1.Type);
        Assert.NotNull(redRook2);
        Assert.Equal(PieceType.Rook, redRook2.Type);

        // Black Rooks
        var blackRook1 = board.GetPieceAt(new Position(0, 9));
        var blackRook2 = board.GetPieceAt(new Position(8, 9));
        Assert.NotNull(blackRook1);
        Assert.Equal(PieceType.Rook, blackRook1.Type);
        Assert.NotNull(blackRook2);
        Assert.Equal(PieceType.Rook, blackRook2.Type);
    }

    [Fact]
    public void ApplyMove_ShouldReturnNewBoardState_AndKeepOriginalBoardImmutable()
    {
        // Arrange
        var initialBoard = BoardState.CreateInitialBoard();
        var redPawnFrom = new Position(0, 3);
        var redPawnTo = new Position(0, 4);

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
