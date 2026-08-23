using XiangqiOnline.Persistence.Services;
using XiangqiOnline.Server.Networking;
using XiangqiOnline.Shared.Enums;

namespace XiangqiOnline.Server.Lobby;

public sealed class GameLifecycleMonitor
{
    private readonly ChallengeManager _challenges;
    private readonly PlayerSessionDirectory _players;
    private readonly IConnectionRegistry _connections;
    private readonly GamePersistenceService _persistence;

    public GameLifecycleMonitor(
        ChallengeManager challenges,
        PlayerSessionDirectory players,
        IConnectionRegistry connections,
        GamePersistenceService persistence)
    {
        _challenges = challenges;
        _players = players;
        _connections = connections;
        _persistence = persistence;
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(250));
        while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
        {
            var nowUtc = DateTimeOffset.UtcNow;
            _challenges.ExpireOverdueChallenges(nowUtc);
            _players.ExpireReconnectWindows(nowUtc);
            _players.PruneDisconnectedSessions(nowUtc, TimeSpan.FromHours(1));
            _challenges.PruneTerminalState(nowUtc, TimeSpan.FromMinutes(10));
            foreach (var room in _challenges.GetRoomsSnapshot(activeOnly: true))
            {
                try
                {
                    await room.ExecuteSerializedAsync(async () =>
                    {
                        var expired = room.Clock.GetExpiredSide();
                        if (expired is null) return false;
                        if (room.IsTerminal) return false;
                        var winner = expired == SideColor.Red ? SideColor.Black : SideColor.Red;
                        var result = new GameResult(winner == SideColor.Red ? "RED_WIN" : "BLACK_WIN", "TIMEOUT", winner,
                            DateTimeOffset.UtcNow, room.Revision, $"{expired} clock reached zero.");
                        if (!room.TryFinish(result)) return false;
                        try
                        {
                            if (_persistence.GetMatch(room.RoomId) is null)
                                _persistence.CreateMatch(room.RoomId, room.RedPlayerId, room.BlackPlayerId, room.RoomId, room.RuleProfileId, room.Clock.Profile.Id);
                            _persistence.CompleteMatch(room.RoomId, result.ResultType, result.EndReason,
                                result.WinnerSide?.ToString().ToUpperInvariant(), result.FinalRevision, result.EndedAtUtc.UtcDateTime);
                        }
                        catch
                        {
                            // Timeout must still release players even when persistence is temporarily unavailable.
                        }
                        _players.LeaveRoom(room.RedPlayerId);
                        _players.LeaveRoom(room.BlackPlayerId);
                        await RoomEventBroadcaster.BroadcastAsync(room, _players, _connections, RoomMessages.GameEnded(room), cancellationToken).ConfigureAwait(false);
                        return true;
                    }, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    ServerConsoleLog.Error("VÒNG ĐỜI", $"Phòng {room.RoomId}: {ex.Message}");
                }
            }
        }
    }
}
