using XiangqiOnline.Server.Lobby;
using XiangqiOnline.Shared.Models;

namespace XiangqiOnline.Server.Tests.Lobby;

public sealed class P1Tv2D5ServerAcceptanceTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 12, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void DuplicateDisplayNameIsRejected()
    {
        var (players, _) = CreateLobby();
        Assert.True(players.Login("Alice", "conn-1", Now).IsSuccess);

        var duplicate = players.Login(" alice ", "conn-2", Now.AddSeconds(1));

        Assert.False(duplicate.IsSuccess);
        Assert.Equal(ErrorCodes.DISPLAY_NAME_TAKEN, duplicate.ErrorCode);
    }

    [Fact]
    public void EmptyDisplayNameIsRejected()
    {
        var (players, _) = CreateLobby();

        var result = players.Login("", "conn-1", Now);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCodes.DISPLAY_NAME_INVALID, result.ErrorCode);
    }

    [Fact]
    public void SelfChallengeIsRejected()
    {
        var (players, manager) = CreateLobby();
        var alice = Login(players, "Alice", "conn-1");

        var result = manager.SendChallenge(alice.PlayerId, alice.PlayerId, "COURSE_DEMO", Now, TimeSpan.FromSeconds(30));

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCodes.PLAYER_NOT_AVAILABLE, result.ErrorCode);
    }

    [Fact]
    public void OfflinePlayerCannotSendChallenge()
    {
        var (players, manager) = CreateLobby();
        var alice = Login(players, "Alice", "conn-1");
        var bob = Login(players, "Bob", "conn-2");
        players.MarkOfflineByConnectionId("conn-1", Now.AddSeconds(1));

        var result = manager.SendChallenge(alice.PlayerId, bob.PlayerId, "COURSE_DEMO", Now.AddSeconds(2), TimeSpan.FromSeconds(30));

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCodes.PLAYER_NOT_AVAILABLE, result.ErrorCode);
    }

    [Fact]
    public void BusyTargetCannotReceiveSecondChallenge()
    {
        var (players, manager) = CreateLobby();
        var alice = Login(players, "Alice", "conn-1");
        var bob = Login(players, "Bob", "conn-2");
        var charlie = Login(players, "Charlie", "conn-3");
        Assert.True(manager.SendChallenge(alice.PlayerId, bob.PlayerId, "COURSE_DEMO", Now, TimeSpan.FromSeconds(30)).IsSuccess);

        var result = manager.SendChallenge(charlie.PlayerId, bob.PlayerId, "COURSE_DEMO", Now.AddSeconds(1), TimeSpan.FromSeconds(30));

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCodes.PLAYER_NOT_AVAILABLE, result.ErrorCode);
    }

    [Fact]
    public void MutualChallengeIsRejectedWhileFirstChallengeIsPending()
    {
        var (players, manager) = CreateLobby();
        var alice = Login(players, "Alice", "conn-1");
        var bob = Login(players, "Bob", "conn-2");
        Assert.True(manager.SendChallenge(alice.PlayerId, bob.PlayerId, "COURSE_DEMO", Now, TimeSpan.FromSeconds(30)).IsSuccess);

        var result = manager.SendChallenge(bob.PlayerId, alice.PlayerId, "COURSE_DEMO", Now.AddSeconds(1), TimeSpan.FromSeconds(30));

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCodes.PLAYER_NOT_AVAILABLE, result.ErrorCode);
        Assert.Equal(PlayerStatus.INVITING, alice.Status);
        Assert.Equal(PlayerStatus.INVITED, bob.Status);
    }

    [Fact]
    public void WrongPlayerCannotAcceptChallenge()
    {
        var (players, manager) = CreateLobby();
        var alice = Login(players, "Alice", "conn-1");
        var bob = Login(players, "Bob", "conn-2");
        var charlie = Login(players, "Charlie", "conn-3");
        var send = manager.SendChallenge(alice.PlayerId, bob.PlayerId, "COURSE_DEMO", Now, TimeSpan.FromSeconds(30));

        var result = manager.AcceptChallenge(send.Challenge!.ChallengeId, charlie.PlayerId, Now.AddSeconds(1));

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCodes.CHALLENGE_NOT_PENDING, result.ErrorCode);
    }

    [Fact]
    public void UnknownChallengeAcceptIsRejected()
    {
        var (_, manager) = CreateLobby();

        var result = manager.AcceptChallenge("missing", "p1", Now);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCodes.CHALLENGE_NOT_FOUND, result.ErrorCode);
    }

    [Fact]
    public void AcceptedChallengeCannotBeAcceptedAgain()
    {
        var (players, manager) = CreateLobby();
        var alice = Login(players, "Alice", "conn-1");
        var bob = Login(players, "Bob", "conn-2");
        var send = manager.SendChallenge(alice.PlayerId, bob.PlayerId, "COURSE_DEMO", Now, TimeSpan.FromSeconds(30));
        Assert.True(manager.AcceptChallenge(send.Challenge!.ChallengeId, bob.PlayerId, Now.AddSeconds(1)).IsSuccess);

        var secondAccept = manager.AcceptChallenge(send.Challenge.ChallengeId, bob.PlayerId, Now.AddSeconds(2));

        Assert.False(secondAccept.IsSuccess);
        Assert.Equal(ErrorCodes.CHALLENGE_NOT_PENDING, secondAccept.ErrorCode);
    }

    [Fact]
    public void RejectCreatesNoRoomAndReleasesBothPlayers()
    {
        var (players, manager) = CreateLobby();
        var alice = Login(players, "Alice", "conn-1");
        var bob = Login(players, "Bob", "conn-2");
        var send = manager.SendChallenge(alice.PlayerId, bob.PlayerId, "COURSE_DEMO", Now, TimeSpan.FromSeconds(30));

        var reject = manager.RejectChallenge(send.Challenge!.ChallengeId, bob.PlayerId);

        Assert.True(reject.IsSuccess);
        Assert.Null(reject.Room);
        Assert.Equal(PlayerStatus.AVAILABLE, alice.Status);
        Assert.Equal(PlayerStatus.AVAILABLE, bob.Status);
    }

    private static (PlayerSessionDirectory Players, ChallengeManager Manager) CreateLobby()
    {
        var nextPlayerId = 0;
        var players = new PlayerSessionDirectory(() => $"p{++nextPlayerId}");
        var nextChallengeId = 0;
        var nextRoomId = 0;
        var manager = new ChallengeManager(
            players,
            () => $"challenge-{++nextChallengeId}",
            () => $"room-{++nextRoomId}");

        return (players, manager);
    }

    private static PlayerSession Login(PlayerSessionDirectory players, string displayName, string connectionId)
    {
        var result = players.Login(displayName, connectionId, Now);
        Assert.True(result.IsSuccess);
        return result.Session!;
    }
}
