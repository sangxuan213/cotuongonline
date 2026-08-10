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

            return LoginResult.Success(session);
        }
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

    public void MarkOfflineByConnectionId(string connectionId, DateTimeOffset nowUtc)
    {
        lock (_gate)
        {
            if (!_sessionsByConnectionId.TryGetValue(connectionId, out var session))
                return;

            session.MarkOffline(nowUtc);
            _activeSessionsByDisplayName.Remove(session.DisplayName);
        }
    }
}
