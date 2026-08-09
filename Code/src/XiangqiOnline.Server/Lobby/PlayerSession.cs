namespace XiangqiOnline.Server.Lobby;

public enum PlayerStatus
{
    AVAILABLE,
    INVITING,
    INVITED,
    IN_GAME,
    OFFLINE
}

public enum PlayerSessionConnectionState
{
    CONNECTED,
    RECONNECTING,
    DISCONNECTED
}

public sealed class PlayerSession
{
    public PlayerSession(string playerId, string displayName, string connectionId, DateTimeOffset connectedAtUtc)
    {
        if (string.IsNullOrWhiteSpace(playerId))
            throw new ArgumentException("Player id is required.", nameof(playerId));
        if (string.IsNullOrWhiteSpace(connectionId))
            throw new ArgumentException("Connection id is required.", nameof(connectionId));

        DisplayName = NormalizeDisplayName(displayName);
        PlayerId = playerId;
        ConnectionId = connectionId;
        ConnectedAtUtc = connectedAtUtc;
        LastSeenAtUtc = connectedAtUtc;
    }

    public string PlayerId { get; }
    public string DisplayName { get; }
    public string ConnectionId { get; private set; }
    public DateTimeOffset ConnectedAtUtc { get; }
    public DateTimeOffset LastSeenAtUtc { get; private set; }
    public PlayerStatus Status { get; private set; } = PlayerStatus.AVAILABLE;
    public PlayerSessionConnectionState ConnectionState { get; private set; } = PlayerSessionConnectionState.CONNECTED;
    public string? ActiveChallengeId { get; private set; }
    public string? RoomId { get; private set; }

    public void MarkInviting(string challengeId)
    {
        EnsureAvailableForChallenge();
        ActiveChallengeId = RequireId(challengeId, nameof(challengeId));
        Status = PlayerStatus.INVITING;
    }

    public void MarkInvited(string challengeId)
    {
        EnsureAvailableForChallenge();
        ActiveChallengeId = RequireId(challengeId, nameof(challengeId));
        Status = PlayerStatus.INVITED;
    }

    public void ClearChallenge()
    {
        if (Status is PlayerStatus.INVITING or PlayerStatus.INVITED)
            Status = PlayerStatus.AVAILABLE;

        ActiveChallengeId = null;
    }

    public void EnterRoom(string roomId)
    {
        RoomId = RequireId(roomId, nameof(roomId));
        ActiveChallengeId = null;
        Status = PlayerStatus.IN_GAME;
    }

    public void LeaveRoom()
    {
        RoomId = null;
        Status = ConnectionState == PlayerSessionConnectionState.CONNECTED
            ? PlayerStatus.AVAILABLE
            : PlayerStatus.OFFLINE;
    }

    public void MarkReconnecting(DateTimeOffset lastSeenAtUtc)
    {
        LastSeenAtUtc = lastSeenAtUtc;
        ConnectionState = PlayerSessionConnectionState.RECONNECTING;
    }

    public void Reconnect(string connectionId, DateTimeOffset reconnectedAtUtc)
    {
        ConnectionId = RequireId(connectionId, nameof(connectionId));
        LastSeenAtUtc = reconnectedAtUtc;
        ConnectionState = PlayerSessionConnectionState.CONNECTED;
    }

    public void MarkOffline(DateTimeOffset lastSeenAtUtc)
    {
        LastSeenAtUtc = lastSeenAtUtc;
        ConnectionState = PlayerSessionConnectionState.DISCONNECTED;
        Status = PlayerStatus.OFFLINE;
        ActiveChallengeId = null;
    }

    public bool CanReceiveChallenge =>
        Status == PlayerStatus.AVAILABLE &&
        ConnectionState == PlayerSessionConnectionState.CONNECTED &&
        RoomId is null &&
        ActiveChallengeId is null;

    public static string NormalizeDisplayName(string displayName)
    {
        var normalized = (displayName ?? string.Empty).Trim();
        if (normalized.Length is < 1 or > 24)
            throw new ArgumentException("Display name must be 1-24 characters.", nameof(displayName));
        if (normalized.Any(char.IsControl))
            throw new ArgumentException("Display name cannot contain control characters.", nameof(displayName));

        return normalized;
    }

    private void EnsureAvailableForChallenge()
    {
        if (!CanReceiveChallenge)
            throw new InvalidOperationException("Player is not available for a challenge.");
    }

    private static string RequireId(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Identifier is required.", parameterName);

        return value;
    }
}
