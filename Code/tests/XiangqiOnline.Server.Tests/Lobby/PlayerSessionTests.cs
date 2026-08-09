using XiangqiOnline.Server.Lobby;

namespace XiangqiOnline.Server.Tests.Lobby;

public sealed class PlayerSessionTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 9, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ConstructorNormalizesDisplayNameAndStartsAvailable()
    {
        var session = new PlayerSession("p1", "  Alice  ", "conn-1", Now);

        Assert.Equal("Alice", session.DisplayName);
        Assert.Equal(PlayerStatus.AVAILABLE, session.Status);
        Assert.True(session.CanReceiveChallenge);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("abcdefghijklmnopqrstuvwxy")]
    [InlineData("Alice\tBob")]
    public void InvalidDisplayNamesAreRejected(string displayName)
    {
        Assert.Throws<ArgumentException>(() => new PlayerSession("p1", displayName, "conn-1", Now));
    }

    [Fact]
    public void MarkInvitingStoresChallengeAndBlocksNewChallenge()
    {
        var session = NewSession();

        session.MarkInviting("challenge-1");

        Assert.Equal(PlayerStatus.INVITING, session.Status);
        Assert.Equal("challenge-1", session.ActiveChallengeId);
        Assert.False(session.CanReceiveChallenge);
    }

    [Fact]
    public void ClearChallengeReturnsInvitingPlayerToAvailable()
    {
        var session = NewSession();
        session.MarkInviting("challenge-1");

        session.ClearChallenge();

        Assert.Equal(PlayerStatus.AVAILABLE, session.Status);
        Assert.Null(session.ActiveChallengeId);
    }

    [Fact]
    public void EnterRoomSetsInGameAndClearsChallengeOwnership()
    {
        var session = NewSession();
        session.MarkInvited("challenge-1");

        session.EnterRoom("room-1");

        Assert.Equal(PlayerStatus.IN_GAME, session.Status);
        Assert.Equal("room-1", session.RoomId);
        Assert.Null(session.ActiveChallengeId);
        Assert.False(session.CanReceiveChallenge);
    }

    [Fact]
    public void OfflineSessionCannotReceiveChallenge()
    {
        var session = NewSession();

        session.MarkOffline(Now.AddSeconds(10));

        Assert.Equal(PlayerStatus.OFFLINE, session.Status);
        Assert.Equal(PlayerSessionConnectionState.DISCONNECTED, session.ConnectionState);
        Assert.False(session.CanReceiveChallenge);
    }

    private static PlayerSession NewSession() => new("p1", "Alice", "conn-1", Now);
}
