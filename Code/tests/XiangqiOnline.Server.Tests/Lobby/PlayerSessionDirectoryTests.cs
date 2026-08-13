using XiangqiOnline.Server.Lobby;
using XiangqiOnline.Shared.Models;

namespace XiangqiOnline.Server.Tests.Lobby;

public sealed class PlayerSessionDirectoryTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 10, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void LoginCreatesAvailableSessionAndBindsItToConnection()
    {
        var directory = NewDirectory("p1");

        var result = directory.Login(" Alice ", "conn-1", Now);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Session);
        Assert.Equal("p1", result.Session.PlayerId);
        Assert.Equal("Alice", result.Session.DisplayName);
        Assert.Equal("conn-1", result.Session.ConnectionId);
        Assert.Equal(PlayerStatus.AVAILABLE, result.Session.Status);
        Assert.True(directory.TryGetByConnectionId("conn-1", out var byConnection));
        Assert.Same(result.Session, byConnection);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("abcdefghijklmnopqrstuvwxy")]
    [InlineData("Alice\nBob")]
    public void LoginRejectsInvalidDisplayName(string displayName)
    {
        var directory = NewDirectory("p1");

        var result = directory.Login(displayName, "conn-1", Now);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCodes.DISPLAY_NAME_INVALID, result.ErrorCode);
        Assert.Equal(0, directory.Count);
    }

    [Fact]
    public void LoginRejectsDuplicateActiveDisplayNameIgnoringCase()
    {
        var nextId = 0;
        var directory = new PlayerSessionDirectory(() => $"p{++nextId}");
        Assert.True(directory.Login("Alice", "conn-1", Now).IsSuccess);

        var result = directory.Login(" alice ", "conn-2", Now.AddSeconds(1));

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCodes.DISPLAY_NAME_TAKEN, result.ErrorCode);
        Assert.Equal(1, directory.Count);
    }

    [Fact]
    public void LoginAllowsDisplayNameAgainAfterPreviousConnectionIsOffline()
    {
        var nextId = 0;
        var directory = new PlayerSessionDirectory(() => $"p{++nextId}");
        Assert.True(directory.Login("Alice", "conn-1", Now).IsSuccess);
        directory.MarkOfflineByConnectionId("conn-1", Now.AddSeconds(5));

        var result = directory.Login("Alice", "conn-2", Now.AddSeconds(6));

        Assert.True(result.IsSuccess);
        Assert.Equal("p2", result.Session!.PlayerId);
        Assert.Equal(2, directory.Count);
    }

    [Fact]
    public void LoginRejectsDuplicateActiveConnection()
    {
        var nextId = 0;
        var directory = new PlayerSessionDirectory(() => $"p{++nextId}");
        Assert.True(directory.Login("Alice", "conn-1", Now).IsSuccess);

        var result = directory.Login("Bob", "conn-1", Now.AddSeconds(1));

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCodes.DUPLICATE_SESSION, result.ErrorCode);
        Assert.Equal(1, directory.Count);
    }

    [Fact]
    public void MarkOfflineByUnknownConnectionDoesNothing()
    {
        var directory = NewDirectory("p1");

        directory.MarkOfflineByConnectionId("missing", Now);

        Assert.Equal(0, directory.Count);
    }

    [Fact]
    public void ReconnectConnectionMovesSessionToNewConnectionAndRemovesStaleMapping()
    {
        var directory = NewDirectory("p1");
        Assert.True(directory.Login("Alice", "conn-1", Now).IsSuccess);

        var result = directory.ReconnectConnection("conn-1", "conn-2", Now.AddSeconds(5));

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Session);
        Assert.Equal("p1", result.Session!.PlayerId);
        Assert.Equal("conn-2", result.Session.ConnectionId);
        Assert.Equal(PlayerStatus.AVAILABLE, result.Session.Status);
        Assert.Equal(PlayerSessionConnectionState.CONNECTED, result.Session.ConnectionState);
        Assert.True(directory.TryGetByConnectionId("conn-2", out var byNewConnection));
        Assert.Same(result.Session, byNewConnection);
        Assert.False(directory.TryGetByConnectionId("conn-1", out _));
    }

    [Fact]
    public void ReconnectConnectionRestoresOfflineSessionToAvailable()
    {
        var directory = NewDirectory("p1");
        Assert.True(directory.Login("Alice", "conn-1", Now).IsSuccess);
        directory.MarkOfflineByConnectionId("conn-1", Now.AddSeconds(5));

        var result = directory.ReconnectConnection("conn-1", "conn-2", Now.AddSeconds(6));

        Assert.True(result.IsSuccess);
        Assert.Equal(PlayerStatus.AVAILABLE, result.Session!.Status);
        Assert.Equal(PlayerSessionConnectionState.CONNECTED, result.Session.ConnectionState);
    }

    [Fact]
    public void ReconnectConnectionKeepsInGameStatusForPlayerInRoom()
    {
        var directory = NewDirectory("p1");
        Assert.True(directory.Login("Alice", "conn-1", Now).IsSuccess);

        Assert.True(directory.EnterRoom("p1", "room-1"));
        directory.MarkOfflineByConnectionId("conn-1", Now.AddSeconds(5));

        var result = directory.ReconnectConnection("conn-1", "conn-2", Now.AddSeconds(6));

        Assert.True(result.IsSuccess);
        Assert.Equal(PlayerStatus.IN_GAME, result.Session!.Status);
        Assert.Equal("room-1", result.Session.RoomId);
    }

    [Fact]
    public void ReconnectConnectionRejectsUnknownCurrentConnection()
    {
        var directory = NewDirectory("p1");

        var result = directory.ReconnectConnection("missing", "conn-2", Now);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCodes.INVALID_SESSION, result.ErrorCode);
    }

    [Fact]
    public void ReconnectConnectionRejectsDuplicateNewConnectionId()
    {
        var nextId = 0;
        var directory = new PlayerSessionDirectory(() => $"p{++nextId}");
        Assert.True(directory.Login("Alice", "conn-1", Now).IsSuccess);
        Assert.True(directory.Login("Bob", "conn-2", Now).IsSuccess);

        var result = directory.ReconnectConnection("conn-1", "conn-2", Now.AddSeconds(5));

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCodes.DUPLICATE_SESSION, result.ErrorCode);
    }

    [Fact]
    public void ReconnectConnectionRejectsDisplayNameTakenByAnotherActiveSession()
    {
        var nextId = 0;
        var directory = new PlayerSessionDirectory(() => $"p{++nextId}");
        Assert.True(directory.Login("Alice", "conn-1", Now).IsSuccess);
        directory.MarkOfflineByConnectionId("conn-1", Now.AddSeconds(5));
        Assert.True(directory.Login("Alice", "conn-2", Now.AddSeconds(6)).IsSuccess);

        var result = directory.ReconnectConnection("conn-1", "conn-3", Now.AddSeconds(7));

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCodes.DISPLAY_NAME_TAKEN, result.ErrorCode);
    }

    private static PlayerSessionDirectory NewDirectory(string playerId) => new(() => playerId);
}
