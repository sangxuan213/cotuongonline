using XiangqiOnline.RuleEngine.Models;
using XiangqiOnline.RuleEngine.Pipeline;
using XiangqiOnline.Shared.Enums;
using XiangqiOnline.Shared.Models;

namespace XiangqiOnline.RuleEngine.Adjudication;

public sealed class LegalMoveGenerator
{
    private readonly MoveValidationPipeline _pipeline;

    public LegalMoveGenerator(MoveValidationPipeline? pipeline = null) =>
        _pipeline = pipeline ?? new MoveValidationPipeline();

    public IReadOnlyList<MoveIntent> Generate(BoardState board, SideColor side)
    {
        ArgumentNullException.ThrowIfNull(board);
        var sideBoard = board.Turn == side ? board : board with { Turn = side };
        var result = new List<MoveIntent>();
        foreach (var piece in sideBoard.GetActivePieces(side).OrderBy(piece => piece.Id, StringComparer.Ordinal))
        {
            for (var y = 0; y <= 9; y++)
            for (var x = 0; x <= 8; x++)
            {
                var to = new Position(x, y);
                var intent = new MoveIntent($"probe-{piece.Id}-{x}-{y}", piece.Position, to, 0);
                if (_pipeline.Validate(sideBoard, intent).IsValid)
                    result.Add(intent);
            }
        }
        return result;
    }

    public bool HasAny(BoardState board, SideColor side) => Generate(board, side).Count > 0;
}
