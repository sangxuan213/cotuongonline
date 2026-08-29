using XiangqiOnline.RuleEngine.Models;
using XiangqiOnline.Shared.Enums;

namespace XiangqiOnline.RuleEngine.Adjudication;

public sealed record PositionFact(
    long Revision,
    BoardState Board,
    SideColor MovedSide,
    MoveClassification Classification);

public sealed record RepetitionDecision(
    bool IsCycle,
    bool ShouldWarn,
    bool IsTerminal,
    SideColor? MustVarySide,
    SideColor? Winner,
    string? EndReason,
    string? CycleSignature,
    string Explanation);

public sealed class RepetitionResolver
{
    public RepetitionDecision Evaluate(IReadOnlyList<PositionFact> history, SideColor? warnedSide = null)
    {
        if (history.Count < 3)
            return None("Chưa đủ lịch sử để hình thành chu kỳ.");

        var current = history[^1];
        var occurrences = history
            .Where(item => BoardFingerprint.EqualsExact(item.Board, current.Board))
            .ToArray();
        if (occurrences.Length < 3)
            return None("Vị trí hiện tại chưa xuất hiện ba lần với cùng bên đến lượt.");

        var cycleStart = occurrences[^3].Revision;
        var cycle = history.Where(item => item.Revision >= cycleStart).ToArray();
        var signature = $"{BoardFingerprint.Hash(current.Board)}:{cycleStart}-{current.Revision}";
        var redLevel = Highest(cycle, SideColor.Red);
        var blackLevel = Highest(cycle, SideColor.Black);

        if (redLevel == blackLevel)
            return new(true, false, true, null, null, "REPETITION_DRAW", signature,
                "Hai bên có cùng mức vi phạm trong chu kỳ; xử hòa theo profile.");

        var offender = redLevel > blackLevel ? SideColor.Red : SideColor.Black;
        if (warnedSide == offender)
        {
            var winner = offender == SideColor.Red ? SideColor.Black : SideColor.Red;
            return new(true, false, true, offender, winner, "REPETITION_VIOLATION", signature,
                "Bên đã được cảnh báo vẫn hoàn tất lại chu kỳ bị cấm.");
        }

        return new(true, true, false, offender, null, null, signature,
            "Phát cảnh báo must-vary cho bên có mức CHECK/CHASE cao hơn.");
    }

    private static int Highest(IEnumerable<PositionFact> cycle, SideColor side) => cycle
        .Where(item => item.MovedSide == side)
        .Select(item => item.Classification switch
        {
            MoveClassification.CHECK => 3,
            MoveClassification.CHASE => 2,
            _ => 1
        })
        .DefaultIfEmpty(1)
        .Max();

    private static RepetitionDecision None(string explanation) =>
        new(false, false, false, null, null, null, null, explanation);
}
