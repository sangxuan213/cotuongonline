using XiangqiOnline.RuleEngine.Models;

namespace XiangqiOnline.RuleEngine.Adjudication;

public enum MoveClassification
{
    CHECK,
    CHASE,
    KILL,
    EXCHANGE,
    BLOCK,
    OFFER,
    CAPTURE,
    IDLE
}

public sealed record MoveClassificationFacts(
    MoveClassification Classification,
    bool IsCheck,
    bool IsCapture,
    string? VictimPieceId,
    string Explanation,
    IReadOnlyList<string>? ChasedVictimIds = null);

public sealed class MoveClassifier
{
    public MoveClassificationFacts Classify(MoveApplicationResult move)
    {
        ArgumentNullException.ThrowIfNull(move);
        if (move.IsCheck)
            return new(MoveClassification.CHECK, true, move.CapturedPiece is not null,
                move.CapturedPiece?.Id, "Nước đi trực tiếp chiếu Tướng; CHECK có độ ưu tiên cao nhất.");
        if (move.CapturedPiece is not null)
            return new(MoveClassification.CAPTURE, false, true,
                move.CapturedPiece.Id, "Nước đi bắt một quân đối phương.");
        var chaseVictims = new ChaseVictimDetector().FindVictims(move);
        if (chaseVictims.Count > 0)
            return new(MoveClassification.CHASE, false, false, chaseVictims[0].Id,
                "Quân vừa đi tạo đòn bắt mới lên quân không được bảo vệ.", chaseVictims.Select(piece => piece.Id).ToArray());
        return new(MoveClassification.IDLE, false, false, null,
            "Không chiếu và không bắt quân; classifier lõi xếp là IDLE.");
    }
}
