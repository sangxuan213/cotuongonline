using XiangqiOnline.Shared.Models;

namespace XiangqiOnline.Server.Lobby;

public sealed class PlayerSessionDirectory
{
    private readonly Func<string> _playerIdFactory;
    private readonly SessionTokenService _tokens;
    private readonly TimeSpan _reconnectWindow;
    private readonly Dictionary<string, PlayerSession> _sessionsByPlayerId = new(StringComparer.Ordinal);
    private readonly Dictionary<string, PlayerSession> _sessionsByConnectionId = new(StringComparer.Ordinal);
    private readonly Dictionary<string, PlayerSession> _activeSessionsByDisplayName = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();

    public PlayerSessionDirectory(
        Func<string>? playerIdFactory = null,
        SessionTokenService? tokens = null,
        TimeSpan? reconnectWindow = null)
    {
        _playerIdFactory = playerIdFactory ?? (() => Guid.NewGuid().ToString("N"));
        _tokens = tokens ?? new SessionTokenService();
        _reconnectWindow = reconnectWindow ?? TimeSpan.FromSeconds(60);
        if (_reconnectWindow <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(reconnectWindow));
    }

    public event Action<PlayerListUpdated>? PlayerListUpdated;

    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _sessionsByPlayerId.Count;
            }
        }
    }

    public LoginResult Login(string displayName, string connectionId, DateTimeOffset nowUtc, string? stablePlayerId = null)
    {
        string normalizedName;
        try
        {
            normalizedName = PlayerSession.NormalizeDisplayName(displayName);
        }
        catch (ArgumentException)
        {
            return LoginResult.Fail(ErrorCodes.DISPLAY_NAME_INVALID, "Display name must be 1-24 visible characters.");
        }

        if (string.IsNullOrWhiteSpace(connectionId))
            return LoginResult.Fail(ErrorCodes.INVALID_SESSION, "Connection id is required.");
        if (stablePlayerId is not null && string.IsNullOrWhiteSpace(stablePlayerId))
            return LoginResult.Fail(ErrorCodes.INVALID_SESSION, "Stable player id is invalid.");

        PlayerListUpdated? update = null;
        LoginResult result;
        lock (_gate)
        {
            if (_sessionsByConnectionId.TryGetValue(connectionId, out var existingConnectionSession) &&
                existingConnectionSession.ConnectionState != PlayerSessionConnectionState.DISCONNECTED)
            {
                return LoginResult.Fail(ErrorCodes.DUPLICATE_SESSION, "Connection already has an active session.");
            }

            if (_activeSessionsByDisplayName.TryGetValue(normalizedName, out var existingNameSession) &&
                existingNameSession.ConnectionState != PlayerSessionConnectionState.DISCONNECTED)
            {
                return LoginResult.Fail(ErrorCodes.DISPLAY_NAME_TAKEN, "Display name is already in use.");
            }

            if (stablePlayerId is not null && _sessionsByPlayerId.TryGetValue(stablePlayerId, out var existingAccountSession))
            {
                if (existingAccountSession.ConnectionState != PlayerSessionConnectionState.DISCONNECTED)
                    return LoginResult.Fail(ErrorCodes.DUPLICATE_SESSION, "Account already has an active session.");
                _sessionsByConnectionId.Remove(existingAccountSession.ConnectionId);
                _activeSessionsByDisplayName.Remove(existingAccountSession.DisplayName);
                _sessionsByPlayerId.Remove(existingAccountSession.PlayerId);
            }

            var session = new PlayerSession(stablePlayerId ?? _playerIdFactory(), normalizedName, connectionId, nowUtc);
            var issuedToken = _tokens.Issue();
            session.SetResumeTokenHash(issuedToken.Hash);
            _sessionsByPlayerId.Add(session.PlayerId, session);
            _sessionsByConnectionId[session.ConnectionId] = session;
            _activeSessionsByDisplayName[session.DisplayName] = session;

            update = CreatePlayerListUpdated(session.PlayerId, "LOGIN_ACCEPTED");
            result = LoginResult.Success(session, issuedToken.PlainText);
        }

        Publish(update);
        return result;
    }

    public bool TryGetByPlayerId(string playerId, out PlayerSession session)
    {
        lock (_gate)
        {
            return _sessionsByPlayerId.TryGetValue(playerId, out session!);
        }
    }

    public bool TryGetByConnectionId(string connectionId, out PlayerSession session)
    {
        lock (_gate)
        {
            return _sessionsByConnectionId.TryGetValue(connectionId, out session!);
        }
    }

    public ReconnectResult ReconnectConnection(string currentConnectionId, string newConnectionId, DateTimeOffset nowUtc)
    {
        if (string.IsNullOrWhiteSpace(newConnectionId))
            return ReconnectResult.Fail(ErrorCodes.INVALID_SESSION, "Connection id is required.");

        PlayerListUpdated? update = null;
        ReconnectResult result;
        lock (_gate)
        {
            if (!_sessionsByConnectionId.TryGetValue(currentConnectionId, out var session))
                return ReconnectResult.Fail(ErrorCodes.INVALID_SESSION, "No session is bound to the current connection.");

            if (_sessionsByConnectionId.TryGetValue(newConnectionId, out var otherConnectionSession) &&
                !ReferenceEquals(otherConnectionSession, session) &&
                otherConnectionSession.ConnectionState != PlayerSessionConnectionState.DISCONNECTED)
            {
                return ReconnectResult.Fail(ErrorCodes.DUPLICATE_SESSION, "New connection id already has an active session.");
            }

            if (_activeSessionsByDisplayName.TryGetValue(session.DisplayName, out var displayNameSession) &&
                !ReferenceEquals(displayNameSession, session) &&
                displayNameSession.ConnectionState != PlayerSessionConnectionState.DISCONNECTED)
            {
                return ReconnectResult.Fail(ErrorCodes.DISPLAY_NAME_TAKEN, "Display name is already in use by another active connection.");
            }

            _sessionsByConnectionId.Remove(session.ConnectionId);
            session.Reconnect(newConnectionId, nowUtc);
            _sessionsByConnectionId[session.ConnectionId] = session;
            _activeSessionsByDisplayName[session.DisplayName] = session;

            update = CreatePlayerListUpdated(session.PlayerId, "PLAYER_RECONNECTED");
            result = ReconnectResult.Success(session);
        }

        Publish(update);
        return result;
    }

    public bool ValidateSessionToken(PlayerSession session, string? token) =>
        session.ResumeTokenHash is not null && token is not null && _tokens.Verify(token, session.ResumeTokenHash);

    public ReconnectResult ResumeByToken(string token, string newConnectionId, DateTimeOffset nowUtc)
    {
        if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(newConnectionId))
            return ReconnectResult.Fail(ErrorCodes.INVALID_SESSION, "Resume token and connection id are required.");

        PlayerListUpdated? update = null;
        ReconnectResult result;
        lock (_gate)
        {
            var session = _sessionsByPlayerId.Values.FirstOrDefault(candidate =>
                candidate.ResumeTokenHash is not null && _tokens.Verify(token, candidate.ResumeTokenHash));
            if (session is null)
                return ReconnectResult.Fail(ErrorCodes.INVALID_SESSION, "Resume token is invalid.");
            if (session.ConnectionState != PlayerSessionConnectionState.RECONNECTING)
                return ReconnectResult.Fail(ErrorCodes.DUPLICATE_SESSION, "Session is not waiting for reconnect.");
            if (session.ReconnectDeadlineUtc is { } deadline && nowUtc > deadline)
            {
                session.MarkOffline(nowUtc);
                _activeSessionsByDisplayName.Remove(session.DisplayName);
                return ReconnectResult.Fail(ErrorCodes.RECONNECT_WINDOW_EXPIRED, "Reconnect window expired.");
            }
            if (_sessionsByConnectionId.TryGetValue(newConnectionId, out var occupied) && !ReferenceEquals(occupied, session))
                return ReconnectResult.Fail(ErrorCodes.DUPLICATE_SESSION, "Connection already belongs to another session.");

            _sessionsByConnectionId.Remove(session.ConnectionId);
            session.Reconnect(newConnectionId, nowUtc);
            _sessionsByConnectionId[newConnectionId] = session;
            _activeSessionsByDisplayName[session.DisplayName] = session;
            update = CreatePlayerListUpdated(session.PlayerId, "PLAYER_RECONNECTED");
            result = ReconnectResult.Success(session);
        }

        Publish(update);
        return result;
    }

    public void MarkOfflineByConnectionId(string connectionId, DateTimeOffset nowUtc)
    {
        PlayerListUpdated? update = null;
        lock (_gate)
        {
            if (!_sessionsByConnectionId.TryGetValue(connectionId, out var session))
                return;

            if (session.RoomId is not null)
            {
                session.MarkReconnecting(nowUtc, _reconnectWindow);
                update = CreatePlayerListUpdated(session.PlayerId, "PLAYER_RECONNECTING");
            }
            else
            {
                session.MarkOffline(nowUtc);
                _activeSessionsByDisplayName.Remove(session.DisplayName);
                update = CreatePlayerListUpdated(session.PlayerId, "PLAYER_OFFLINE");
            }
        }

        Publish(update);
    }

    public IReadOnlyList<string> ExpireReconnectWindows(DateTimeOffset nowUtc)
    {
        var expiredPlayerIds = new List<string>();
        var updates = new List<PlayerListUpdated>();
        lock (_gate)
        {
            foreach (var session in _sessionsByPlayerId.Values.Where(session =>
                         session.ConnectionState == PlayerSessionConnectionState.RECONNECTING &&
                         session.ReconnectDeadlineUtc is { } deadline && nowUtc >= deadline).ToArray())
            {
                _sessionsByConnectionId.Remove(session.ConnectionId);
                session.MarkOffline(nowUtc);
                _activeSessionsByDisplayName.Remove(session.DisplayName);
                expiredPlayerIds.Add(session.PlayerId);
                updates.Add(CreatePlayerListUpdated(session.PlayerId, "RECONNECT_WINDOW_EXPIRED"));
            }
        }

        foreach (var update in updates) Publish(update);
        return expiredPlayerIds;
    }

    public int PruneDisconnectedSessions(DateTimeOffset nowUtc, TimeSpan retention)
    {
        lock (_gate)
        {
            var stale = _sessionsByPlayerId.Values.Where(session =>
                session.ConnectionState == PlayerSessionConnectionState.DISCONNECTED &&
                session.RoomId is null && nowUtc - session.LastSeenAtUtc >= retention).ToArray();
            foreach (var session in stale)
            {
                _sessionsByPlayerId.Remove(session.PlayerId);
                _sessionsByConnectionId.Remove(session.ConnectionId);
                _activeSessionsByDisplayName.Remove(session.DisplayName);
            }
            return stale.Length;
        }
    }

    public IReadOnlyList<PlayerDirectoryEntry> GetSnapshot()
    {
        lock (_gate)
        {
            return CreateSnapshot();
        }
    }

    public bool MarkInviting(string playerId, string challengeId)
    {
        return UpdatePlayerStatus(playerId, session => session.MarkInviting(challengeId), "PLAYER_INVITING");
    }

    public bool MarkInvited(string playerId, string challengeId)
    {
        return UpdatePlayerStatus(playerId, session => session.MarkInvited(challengeId), "PLAYER_INVITED");
    }

    public bool ClearChallenge(string playerId)
    {
        return UpdatePlayerStatus(playerId, session => session.ClearChallenge(), "CHALLENGE_CLEARED");
    }

    public bool EnterRoom(string playerId, string roomId)
    {
        return UpdatePlayerStatus(playerId, session => session.EnterRoom(roomId), "PLAYER_IN_GAME");
    }

    public bool LeaveRoom(string playerId)
    {
        return UpdatePlayerStatus(playerId, session => session.LeaveRoom(), "PLAYER_LEFT_ROOM");
    }

    private bool UpdatePlayerStatus(string playerId, Action<PlayerSession> updateSession, string reason)
    {
        PlayerListUpdated? update = null;
        lock (_gate)
        {
            if (!_sessionsByPlayerId.TryGetValue(playerId, out var session))
                return false;

            updateSession(session);
            update = CreatePlayerListUpdated(session.PlayerId, reason);
        }

        Publish(update);
        return true;
    }

    private PlayerListUpdated CreatePlayerListUpdated(string changedPlayerId, string reason) =>
        new(changedPlayerId, reason, CreateSnapshot());

    private IReadOnlyList<PlayerDirectoryEntry> CreateSnapshot() =>
        _sessionsByPlayerId.Values
            .Select(session => new PlayerDirectoryEntry(
                session.PlayerId,
                session.DisplayName,
                session.Status,
                session.ConnectionState))
            .OrderBy(player => player.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(player => player.PlayerId, StringComparer.Ordinal)
            .ToArray();

    private void Publish(PlayerListUpdated? update)
    {
        if (update is not null)
            PlayerListUpdated?.Invoke(update);
    }
}
