using System.Collections.Immutable;
using XiangqiOnline.RuleEngine.Adjudication;
using XiangqiOnline.RuleEngine.Models;
using XiangqiOnline.RuleEngine.Pipeline;
using XiangqiOnline.Shared.Enums;
using XiangqiOnline.Shared.Models;

namespace XiangqiOnline.RuleEngine.Tests.Adjudication;

public sealed class ExperiencedPlayerScenarioTests
{
    [Fact]
    public void ForcedMatePosition_IsRecognizedAsCheckmate()
    {
        var board = Board(SideColor.Black,
            Piece("BLACK_GENERAL", PieceType.General, SideColor.Black, 4, 0),
            Piece("RED_GENERAL", PieceType.General, SideColor.Red, 4, 9),
            Piece("RED_PAWN", PieceType.Pawn, SideColor.Red, 4, 1),
            Piece("RED_CHARIOT_LEFT", PieceType.Chariot, SideColor.Red, 3, 1),
            Piece("RED_CHARIOT_RIGHT", PieceType.Chariot, SideColor.Red, 5, 1));

        var result = new GameTerminationDetector().Evaluate(board);

        Assert.True(result.IsTerminal);
        Assert.True(result.IsCheck);
        Assert.Equal(SideColor.Red, result.Winner);
        Assert.Equal("CHECKMATE", result.EndReason);
        Assert.Equal(0, result.LegalMoveCount);
    }

    [Fact]
    public void TrappedGeneralWithoutCheck_IsRecognizedAsStalemate()
    {
        var board = Board(SideColor.Black,
            Piece("BLACK_GENERAL", PieceType.General, SideColor.Black, 4, 0),
            Piece("RED_GENERAL", PieceType.General, SideColor.Red, 4, 9),
            Piece("RED_BLOCKER", PieceType.Pawn, SideColor.Red, 4, 5),
            Piece("RED_CHARIOT_LEFT", PieceType.Chariot, SideColor.Red, 3, 1),
            Piece("RED_CHARIOT_RIGHT", PieceType.Chariot, SideColor.Red, 5, 1));

        var result = new GameTerminationDetector().Evaluate(board);

        Assert.True(result.IsTerminal);
        Assert.False(result.IsCheck);
        Assert.Equal(SideColor.Red, result.Winner);
        Assert.Equal("STALEMATE_NO_LEGAL_MOVE", result.EndReason);
    }

    [Fact]
    public void SeededRandomPlayouts_PreserveCoreBoardInvariants()
    {
        var random = new Random(1806);
        var generator = new LegalMoveGenerator();
        var pipeline = new MoveValidationPipeline();
        var termination = new GameTerminationDetector(generator);
        var committedMoves = 0;

        for (var game = 0; game < 12; game++)
        {
            var board = BoardState.CreateInitialBoard();
            for (var ply = 0; ply < 120; ply++)
            {
                var terminal = termination.Evaluate(board);
                if (terminal.IsTerminal) break;
                var legal = generator.Generate(board, board.Turn);
                Assert.NotEmpty(legal);
                var move = legal[random.Next(legal.Count)];
                Assert.True(pipeline.Validate(board, move).IsValid);
                var before = BoardFingerprint.Hash(board);
                board = board.ApplyMove(move.From, move.To);
                Assert.NotEqual(before, BoardFingerprint.Hash(board));
                Assert.Single(board.GetActivePieces(SideColor.Red).Where(piece => piece.Type == PieceType.General));
                Assert.Single(board.GetActivePieces(SideColor.Black).Where(piece => piece.Type == PieceType.General));
                committedMoves++;
            }
        }

        Assert.True(committedMoves > 500, $"Only {committedMoves} moves were exercised.");
    }

    private static BoardState Board(SideColor turn, params PieceState[] pieces) =>
        new(pieces.ToImmutableDictionary(piece => piece.Position, piece => piece), turn);

    private static PieceState Piece(string id, PieceType type, SideColor side, int x, int y) =>
        new(id, type, side, new Position(x, y));
}
