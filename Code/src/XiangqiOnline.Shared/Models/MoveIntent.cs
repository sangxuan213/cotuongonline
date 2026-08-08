namespace XiangqiOnline.Shared.Models;

/// <summary>
/// Ý định di chuyển nước đi từ Client theo Shared Wire Contract.
/// </summary>
public record MoveIntent(
    string ClientMoveId,
    Position From,
    Position To,
    long ExpectedRevision = 0
);
