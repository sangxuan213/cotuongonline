namespace XiangqiOnline.Server.Lobby;

public enum ChallengeStatus
{
    PENDING,
    ACCEPTED,
    REJECTED,
    CANCELLED,
    EXPIRED
}

public sealed class Challenge
{
    public Challenge(
        string challengeId,
        string challengerPlayerId,
        string targetPlayerId,
        string timeProfile,
        DateTimeOffset createdAtUtc,
        DateTimeOffset expiresAtUtc)
    {
        if (string.IsNullOrWhiteSpace(challengeId))
            throw new ArgumentException("Challenge id is required.", nameof(challengeId));
        if (string.IsNullOrWhiteSpace(challengerPlayerId))
            throw new ArgumentException("Challenger player id is required.", nameof(challengerPlayerId));
        if (string.IsNullOrWhiteSpace(targetPlayerId))
            throw new ArgumentException("Target player id is required.", nameof(targetPlayerId));
        if (challengerPlayerId == targetPlayerId)
            throw new ArgumentException("A player cannot challenge themselves.", nameof(targetPlayerId));
        if (string.IsNullOrWhiteSpace(timeProfile))
            throw new ArgumentException("Time profile is required.", nameof(timeProfile));
        if (expiresAtUtc <= createdAtUtc)
            throw new ArgumentException("Challenge expiry must be after creation time.", nameof(expiresAtUtc));

        ChallengeId = challengeId;
        ChallengerPlayerId = challengerPlayerId;
        TargetPlayerId = targetPlayerId;
        TimeProfile = timeProfile;
        CreatedAtUtc = createdAtUtc;
        ExpiresAtUtc = expiresAtUtc;
    }

    public string ChallengeId { get; }
    public string ChallengerPlayerId { get; }
    public string TargetPlayerId { get; }
    public string TimeProfile { get; }
    public DateTimeOffset CreatedAtUtc { get; }
    public DateTimeOffset ExpiresAtUtc { get; }
    public ChallengeStatus Status { get; private set; } = ChallengeStatus.PENDING;

    public bool IsPending => Status == ChallengeStatus.PENDING;

    public bool Involves(string playerId) =>
        ChallengerPlayerId == playerId || TargetPlayerId == playerId;

    public bool CanBeAcceptedBy(string playerId, DateTimeOffset nowUtc) =>
        IsPending && TargetPlayerId == playerId && nowUtc < ExpiresAtUtc;

    public void Accept(string playerId, DateTimeOffset nowUtc)
    {
        EnsurePending();
        if (TargetPlayerId != playerId)
            throw new InvalidOperationException("Only the challenged player can accept this challenge.");
        if (nowUtc >= ExpiresAtUtc)
            throw new InvalidOperationException("Expired challenges cannot be accepted.");

        Status = ChallengeStatus.ACCEPTED;
    }

    public void Reject(string playerId)
    {
        EnsurePending();
        if (TargetPlayerId != playerId)
            throw new InvalidOperationException("Only the challenged player can reject this challenge.");

        Status = ChallengeStatus.REJECTED;
    }

    public void Cancel(string playerId)
    {
        EnsurePending();
        if (ChallengerPlayerId != playerId)
            throw new InvalidOperationException("Only the challenger can cancel this challenge.");

        Status = ChallengeStatus.CANCELLED;
    }

    public bool Expire(DateTimeOffset nowUtc)
    {
        if (!IsPending || nowUtc < ExpiresAtUtc)
            return false;

        Status = ChallengeStatus.EXPIRED;
        return true;
    }

    private void EnsurePending()
    {
        if (!IsPending)
            throw new InvalidOperationException("Only pending challenges can transition.");
    }
}
