using XiangqiOnline.Persistence.Models;
using XiangqiOnline.RuleEngine.Adjudication;

namespace XiangqiOnline.Persistence.Services;

public enum MoveCommitStatus
{
    Committed,
    Duplicate,
    Rejected,
    PersistenceFailure
}

/// <summary>
/// Kết quả của một lần commit nước đi.
/// </summary>
public sealed record MoveCommitResult(
    MoveCommitStatus Status,
    MoveRecord? Move = null,
    long Revision = 0,
    string? ErrorCode = null,
    string? Message = null,
    GameResult? FinalResult = null)
{
    public bool IsCommitted => Status == MoveCommitStatus.Committed;
    public bool IsDuplicate => Status == MoveCommitStatus.Duplicate;
    public bool IsRejected => Status == MoveCommitStatus.Rejected;
    public bool IsPersistenceFailure => Status == MoveCommitStatus.PersistenceFailure;
}
