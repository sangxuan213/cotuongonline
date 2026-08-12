using XiangqiOnline.Server.Lobby;

namespace XiangqiOnline.Server.Tests.Lobby;

public sealed class PlayerDirectoryBroadcastTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 10, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void LoginPublishesPlayerListUpdateWithPublicSnapshot()
    {
        var directory = NewDirectory();
        PlayerListUpdated? update = null;
        directory.PlayerListUpdated += published => update = published;

        var result = directory.Login("Alice", "conn-1", Now);

        Assert.True(result.IsSuccess);
        Assert.NotNull(update);
        Assert.Equal("p1", update.ChangedPlayerId);
        Assert.Equal("LOGIN_ACCEPTED", update.Reason);
        var player = Assert.Single(update.Players);
        Assert.Equal("p1", player.PlayerId);
        Assert.Equal("Alice", player.DisplayName);
        Assert.Equal(PlayerStatus.AVAILABLE, player.Status);
        Assert.Equal(PlayerSessionConnectionState.CONNECTED, player.ConnectionState);
    }

    [Fact]
    public void GetSnapshotReturnsPlayersSortedByDisplayName()
    {
        var directory = NewDirectory();
        Assert.True(directory.Login("Charlie", "conn-1", Now).IsSuccess);
        Assert.True(directory.Login("alice", "conn-2", Now).IsSuccess);
        Assert.True(directory.Login("Bob", "conn-3", Now).IsSuccess);

        var snapshot = directory.GetSnapshot();

        Assert.Collection(
            snapshot,
            player => Assert.Equal("alice", player.DisplayName),
            player => Assert.Equal("Bob", player.DisplayName),
            player => Assert.Equal("Charlie", player.DisplayName));
    }

    [Fact]
    public void StatusChangePublishesUpdatedSnapshot()
    {
        var directory = NewDirectory();
        Assert.True(directory.Login("Alice", "conn-1", Now).IsSuccess);
        PlayerListUpdated? update = null;
        directory.PlayerListUpdated += published => update = published;

        var changed = directory.MarkInviting("p1", "challenge-1");

        Assert.True(changed);
        Assert.NotNull(update);
        Assert.Equal("PLAYER_INVITING", update.Reason);
        Assert.Equal(PlayerStatus.INVITING, Assert.Single(update.Players).Status);
    }

    [Fact]
    public void MarkOfflinePublishesOfflineSnapshotAndReleasesDisplayName()
    {
        var directory = NewDirectory();
        Assert.True(directory.Login("Alice", "conn-1", Now).IsSuccess);
        PlayerListUpdated? update = null;
        directory.PlayerListUpdated += published => update = published;

        directory.MarkOfflineByConnectionId("conn-1", Now.AddSeconds(5));

        Assert.NotNull(update);
        Assert.Equal("PLAYER_OFFLINE", update.Reason);
        var player = Assert.Single(update.Players);
        Assert.Equal(PlayerStatus.OFFLINE, player.Status);
        Assert.Equal(PlayerSessionConnectionState.DISCONNECTED, player.ConnectionState);
        Assert.True(directory.Login("Alice", "conn-2", Now.AddSeconds(6)).IsSuccess);
    }

    [Fact]
    public void UnknownPlayerStatusChangeDoesNotPublish()
    {
        var directory = NewDirectory();
        var publishCount = 0;
        directory.PlayerListUpdated += _ => publishCount++;

        var changed = directory.EnterRoom("missing", "room-1");

        Assert.False(changed);
        Assert.Equal(0, publishCount);
    }

    private static PlayerSessionDirectory NewDirectory()
    {
        var nextId = 0;
        return new PlayerSessionDirectory(() => $"p{++nextId}");
    }
}
