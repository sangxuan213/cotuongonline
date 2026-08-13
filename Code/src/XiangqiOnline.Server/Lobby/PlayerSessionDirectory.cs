using XiangqiOnline.Shared.Models;

namespace XiangqiOnline.Server.Lobby;

public sealed class PlayerSessionDirectory
{
    private readonly Func<string> _playerIdFactory;
    private readonly Dictionary<string, PlayerSession> _sessionsByPlayerId = new(StringComparer.Ordinal);
    private readonly Dictionary<string, PlayerSession> _sessionsByConnectionId = new(StringComparer.Ordinal);
    private readonly Dictionary<string, PlayerSession> _activeSessionsByDisplayName = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();

    public PlayerSessionDirectory(Func<string>? playerIdFactory = null)
    {
        _playerIdFactory = playerIdFactory ?? (() => Guid.NewGuid().ToString("N"));
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

    public LoginResult Login(string displayName, string connectionId, DateTimeOffset nowUtc)
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

            var session = new PlayerSession(_playerIdFactory(), normalizedName, connectionId, nowUtc);
            _sessionsByPlayerId.Add(session.PlayerId, session);
            _sessionsByConnectionId[session.ConnectionId] = session;
            _activeSessionsByDisplayName[session.DisplayName] = session;

            update = CreatePlayerListUpdated(session.PlayerId, "LOGIN_ACCEPTED");
            result = LoginResult.Success(session);
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

    public void MarkOfflineByConnectionId(string connectionId, DateTimeOffset nowUtc)
    {
        PlayerListUpdated? update = null;
        lock (_gate)
        {
            if (!_sessionsByConnectionId.TryGetValue(connectionId, out var session))
                return;

            session.MarkOffline(nowUtc);
            _activeSessionsByDisplayName.Remove(session.DisplayName);
            update = CreatePlayerListUpdated(session.PlayerId, "PLAYER_OFFLINE");
        }

        Publish(update);
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
