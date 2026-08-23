using XiangqiOnline.Server.Lobby;
using XiangqiOnline.Shared.Enums;
using XiangqiOnline.Shared.Models;

namespace XiangqiOnline.Server.Tests.Lobby;

public sealed class ChallengeManagerTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 12, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void SendChallengeMarksBothPlayersAndStoresPendingChallenge()
    {
        var (players, manager) = CreateLobby();
        var red = Login(players, "Alice", "conn-1");
        var black = Login(players, "Bob", "conn-2");

        var result = manager.SendChallenge(red.PlayerId, black.PlayerId, "COURSE_DEMO", Now, TimeSpan.FromSeconds(30));

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Challenge);
        Assert.Equal("challenge-1", result.Challenge.ChallengeId);
        Assert.Equal(ChallengeStatus.PENDING, result.Challenge.Status);
        Assert.Equal(PlayerStatus.INVITING, red.Status);
        Assert.Equal(PlayerStatus.INVITED, black.Status);
        Assert.True(manager.TryGetChallenge("challenge-1", out var stored));
        Assert.Same(result.Challenge, stored);
    }

    [Fact]
    public void SendChallengeRejectsSelfChallenge()
    {
        var (players, manager) = CreateLobby();
        var player = Login(players, "Alice", "conn-1");

        var result = manager.SendChallenge(player.PlayerId, player.PlayerId, "COURSE_DEMO", Now, TimeSpan.FromSeconds(30));

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCodes.PLAYER_NOT_AVAILABLE, result.ErrorCode);
    }

    [Fact]
    public void SendChallengeRejectsBusyTarget()
    {
        var (players, manager) = CreateLobby();
        var alice = Login(players, "Alice", "conn-1");
        var bob = Login(players, "Bob", "conn-2");
        var charlie = Login(players, "Charlie", "conn-3");
        Assert.True(manager.SendChallenge(alice.PlayerId, bob.PlayerId, "COURSE_DEMO", Now, TimeSpan.FromSeconds(30)).IsSuccess);

        var result = manager.SendChallenge(charlie.PlayerId, bob.PlayerId, "COURSE_DEMO", Now, TimeSpan.FromSeconds(30));

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCodes.PLAYER_NOT_AVAILABLE, result.ErrorCode);
    }

    [Fact]
    public void AcceptChallengeCreatesPlayingRoomWithInitialBoardAndRedTurn()
    {
        var (players, manager) = CreateLobby();
        var red = Login(players, "Alice", "conn-1");
        var black = Login(players, "Bob", "conn-2");
        var send = manager.SendChallenge(red.PlayerId, black.PlayerId, "COURSE_DEMO", Now, TimeSpan.FromSeconds(30));

        var accept = manager.AcceptChallenge(send.Challenge!.ChallengeId, black.PlayerId, Now.AddSeconds(1));

        Assert.True(accept.IsSuccess);
        Assert.NotNull(accept.Room);
        Assert.Equal(ChallengeStatus.ACCEPTED, accept.Challenge!.Status);
        Assert.Equal("room-1", accept.Room.RoomId);
        Assert.Equal(red.PlayerId, accept.Room.RedPlayerId);
        Assert.Equal(black.PlayerId, accept.Room.BlackPlayerId);
        Assert.Equal(GameRoomStatus.PLAYING, accept.Room.Status);
        Assert.Equal(SideColor.Red, accept.Room.CurrentTurn);
        Assert.Equal(SideColor.Red, accept.Room.Board.Turn);
        Assert.Equal(32, accept.Room.Board.GetActivePieces().Count());
        Assert.Equal(PlayerStatus.IN_GAME, red.Status);
        Assert.Equal(PlayerStatus.IN_GAME, black.Status);
        Assert.Equal("room-1", red.RoomId);
        Assert.Equal("room-1", black.RoomId);
        Assert.True(manager.TryGetRoom("room-1", out var storedRoom));
        Assert.Same(accept.Room, storedRoom);
    }

    [Fact]
    public void RejectChallengeReturnsBothPlayersToAvailable()
    {
        var (players, manager) = CreateLobby();
        var alice = Login(players, "Alice", "conn-1");
        var bob = Login(players, "Bob", "conn-2");
        var send = manager.SendChallenge(alice.PlayerId, bob.PlayerId, "COURSE_DEMO", Now, TimeSpan.FromSeconds(30));

        var reject = manager.RejectChallenge(send.Challenge!.ChallengeId, bob.PlayerId, Now.AddSeconds(5));

        Assert.True(reject.IsSuccess);
        Assert.Equal(ChallengeStatus.REJECTED, reject.Challenge!.Status);
        Assert.Equal(PlayerStatus.AVAILABLE, alice.Status);
        Assert.Equal(PlayerStatus.AVAILABLE, bob.Status);
        Assert.Null(alice.ActiveChallengeId);
        Assert.Null(bob.ActiveChallengeId);
    }

    [Fact]
    public void AcceptUnknownChallengeReturnsNotFound()
    {
        var (_, manager) = CreateLobby();

        var result = manager.AcceptChallenge("missing", "p1", Now);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCodes.CHALLENGE_NOT_FOUND, result.ErrorCode);
    }

    [Fact]
    public void RejectAfterAcceptReturnsNotPending()
    {
        var (players, manager) = CreateLobby();
        var alice = Login(players, "Alice", "conn-1");
        var bob = Login(players, "Bob", "conn-2");
        var send = manager.SendChallenge(alice.PlayerId, bob.PlayerId, "COURSE_DEMO", Now, TimeSpan.FromSeconds(30));
        Assert.True(manager.AcceptChallenge(send.Challenge!.ChallengeId, bob.PlayerId, Now.AddSeconds(1)).IsSuccess);

        var result = manager.RejectChallenge(send.Challenge.ChallengeId, bob.PlayerId, Now.AddSeconds(5));

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCodes.CHALLENGE_NOT_PENDING, result.ErrorCode);
    }

    [Fact]
    public void AcceptAfterExpiryExpiresChallengeAndClearsBothPlayers()
    {
        var (players, manager) = CreateLobby();
        var alice = Login(players, "Alice", "conn-1");
        var bob = Login(players, "Bob", "conn-2");
        var send = manager.SendChallenge(alice.PlayerId, bob.PlayerId, "COURSE_DEMO", Now, TimeSpan.FromSeconds(30));

        var result = manager.AcceptChallenge(send.Challenge!.ChallengeId, bob.PlayerId, Now.AddSeconds(31));

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCodes.CHALLENGE_EXPIRED, result.ErrorCode);
        Assert.Equal(ChallengeStatus.EXPIRED, send.Challenge.Status);
        Assert.Equal(PlayerStatus.AVAILABLE, alice.Status);
        Assert.Equal(PlayerStatus.AVAILABLE, bob.Status);
        Assert.Null(alice.ActiveChallengeId);
        Assert.Null(bob.ActiveChallengeId);
    }

    [Fact]
    public void AcceptByNonTargetPlayerReturnsUnauthorized()
    {
        var (players, manager) = CreateLobby();
        var alice = Login(players, "Alice", "conn-1");
        var bob = Login(players, "Bob", "conn-2");
        var charlie = Login(players, "Charlie", "conn-3");
        var send = manager.SendChallenge(alice.PlayerId, bob.PlayerId, "COURSE_DEMO", Now, TimeSpan.FromSeconds(30));

        var result = manager.AcceptChallenge(send.Challenge!.ChallengeId, alice.PlayerId, Now.AddSeconds(1));

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCodes.CHALLENGE_UNAUTHORIZED, result.ErrorCode);
        Assert.Equal(ChallengeStatus.PENDING, send.Challenge.Status);
    }

    [Fact]
    public void RejectByNonTargetPlayerReturnsUnauthorized()
    {
        var (players, manager) = CreateLobby();
        var alice = Login(players, "Alice", "conn-1");
        var bob = Login(players, "Bob", "conn-2");
        var charlie = Login(players, "Charlie", "conn-3");
        var send = manager.SendChallenge(alice.PlayerId, bob.PlayerId, "COURSE_DEMO", Now, TimeSpan.FromSeconds(30));

        var result = manager.RejectChallenge(send.Challenge!.ChallengeId, alice.PlayerId, Now.AddSeconds(5));

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCodes.CHALLENGE_UNAUTHORIZED, result.ErrorCode);
        Assert.Equal(ChallengeStatus.PENDING, send.Challenge.Status);
    }

    [Fact]
    public void SendChallengeWithNonPositiveLifetimeThrows()
    {
        var (players, manager) = CreateLobby();
        var alice = Login(players, "Alice", "conn-1");
        var bob = Login(players, "Bob", "conn-2");

        Assert.Throws<ArgumentException>(() =>
            manager.SendChallenge(alice.PlayerId, bob.PlayerId, "COURSE_DEMO", Now, TimeSpan.Zero));
    }

    [Fact]
    public void CancelChallengeReleasesBothPlayers()
    {
        var (players, manager) = CreateLobby();
        var alice = Login(players, "Alice", "conn-1");
        var bob = Login(players, "Bob", "conn-2");
        var send = manager.SendChallenge(alice.PlayerId, bob.PlayerId, "COURSE_DEMO", Now, TimeSpan.FromSeconds(30));

        var result = manager.CancelChallenge(send.Challenge!.ChallengeId, alice.PlayerId);

        Assert.True(result.IsSuccess);
        Assert.Equal(ChallengeStatus.CANCELLED, send.Challenge.Status);
        Assert.Equal(PlayerStatus.AVAILABLE, alice.Status);
        Assert.Equal(PlayerStatus.AVAILABLE, bob.Status);
        Assert.Null(alice.ActiveChallengeId);
        Assert.Null(bob.ActiveChallengeId);
    }

    [Fact]
    public void CancelByNonChallengerReturnsUnauthorized()
    {
        var (players, manager) = CreateLobby();
        var alice = Login(players, "Alice", "conn-1");
        var bob = Login(players, "Bob", "conn-2");
        var send = manager.SendChallenge(alice.PlayerId, bob.PlayerId, "COURSE_DEMO", Now, TimeSpan.FromSeconds(30));

        var result = manager.CancelChallenge(send.Challenge!.ChallengeId, bob.PlayerId);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCodes.CHALLENGE_UNAUTHORIZED, result.ErrorCode);
    }

    [Fact]
    public void ExpireOverdueChallengesSweepsAndClearsPlayers()
    {
        var (players, manager) = CreateLobby();
        var alice = Login(players, "Alice", "conn-1");
        var bob = Login(players, "Bob", "conn-2");
        var send = manager.SendChallenge(alice.PlayerId, bob.PlayerId, "COURSE_DEMO", Now, TimeSpan.FromSeconds(30));

        manager.ExpireOverdueChallenges(Now.AddSeconds(31));

        Assert.Equal(ChallengeStatus.EXPIRED, send.Challenge!.Status);
        Assert.Equal(PlayerStatus.AVAILABLE, alice.Status);
        Assert.Equal(PlayerStatus.AVAILABLE, bob.Status);
    }

    [Fact]
    public void AcceptedRematchCreatesNewRoomAndSwapsPlayerColors()
    {
        var (players, manager) = CreateLobby();
        var alice = Login(players, "Alice", "conn-1");
        var bob = Login(players, "Bob", "conn-2");
        var challenge = manager.SendChallenge(alice.PlayerId, bob.PlayerId, "10+0", Now, TimeSpan.FromSeconds(30));
        var original = manager.AcceptChallenge(challenge.Challenge!.ChallengeId, bob.PlayerId, Now.AddSeconds(1)).Room!;
        Assert.True(original.TryFinish(new GameResult("RED_WIN", "CHECKMATE", SideColor.Red, Now.AddMinutes(1), 7, "Mate")));
        players.LeaveRoom(alice.PlayerId);
        players.LeaveRoom(bob.PlayerId);

        Assert.True(manager.TryRequestRematch(original.RoomId, alice.PlayerId, Now.AddMinutes(1),
            TimeSpan.FromSeconds(60), out _, out _));
        Assert.Equal(PlayerStatus.INVITING, alice.Status);
        Assert.Equal(PlayerStatus.INVITED, bob.Status);

        Assert.True(manager.TryRespondToRematch(original.RoomId, bob.PlayerId, true, Now.AddMinutes(1).AddSeconds(2),
            out _, out var rematch, out var error), error);
        Assert.NotNull(rematch);
        Assert.NotEqual(original.RoomId, rematch!.RoomId);
        Assert.Equal(original.BlackPlayerId, rematch.RedPlayerId);
        Assert.Equal(original.RedPlayerId, rematch.BlackPlayerId);
        Assert.Equal("10+0", rematch.TimeProfile);
        Assert.Equal(GameRoomStatus.PLAYING, rematch.Status);
        Assert.Equal(PlayerStatus.IN_GAME, alice.Status);
        Assert.Equal(PlayerStatus.IN_GAME, bob.Status);
    }

    [Fact]
    public void RematchRequiresOpponentResponseAndRejectReleasesBothPlayers()
    {
        var (players, manager) = CreateLobby();
        var alice = Login(players, "Alice", "conn-1");
        var bob = Login(players, "Bob", "conn-2");
        var challenge = manager.SendChallenge(alice.PlayerId, bob.PlayerId, "10+0", Now, TimeSpan.FromSeconds(30));
        var original = manager.AcceptChallenge(challenge.Challenge!.ChallengeId, bob.PlayerId, Now.AddSeconds(1)).Room!;
        Assert.True(original.TryFinish(new GameResult("DRAW", "DRAW_AGREEMENT", null, Now.AddMinutes(1), 2, "Draw")));
        players.LeaveRoom(alice.PlayerId);
        players.LeaveRoom(bob.PlayerId);
        Assert.True(manager.TryRequestRematch(original.RoomId, alice.PlayerId, Now.AddMinutes(1),
            TimeSpan.FromSeconds(60), out _, out _));

        Assert.False(manager.TryRespondToRematch(original.RoomId, alice.PlayerId, true, Now.AddMinutes(1).AddSeconds(1),
            out _, out _, out _));
        Assert.True(manager.TryRespondToRematch(original.RoomId, bob.PlayerId, false, Now.AddMinutes(1).AddSeconds(2),
            out _, out var room, out _));
        Assert.Null(room);
        Assert.Equal(PlayerStatus.AVAILABLE, alice.Status);
        Assert.Equal(PlayerStatus.AVAILABLE, bob.Status);
    }

    [Fact]
    public async Task ConcurrentRematchAcceptsCreateExactlyOneNewRoom()
    {
        var (players, manager) = CreateLobby();
        var alice = Login(players, "Alice", "conn-1");
        var bob = Login(players, "Bob", "conn-2");
        var challenge = manager.SendChallenge(alice.PlayerId, bob.PlayerId, "10+0", Now, TimeSpan.FromSeconds(30));
        var original = manager.AcceptChallenge(challenge.Challenge!.ChallengeId, bob.PlayerId, Now.AddSeconds(1)).Room!;
        Assert.True(original.TryFinish(new GameResult("RED_WIN", "CHECKMATE", SideColor.Red, Now.AddMinutes(1), 8, "Mate")));
        players.LeaveRoom(alice.PlayerId);
        players.LeaveRoom(bob.PlayerId);
        Assert.True(manager.TryRequestRematch(original.RoomId, alice.PlayerId, Now.AddMinutes(1),
            TimeSpan.FromSeconds(60), out _, out _));

        async Task<(bool Success, GameRoom? Room)> AcceptAsync()
        {
            await Task.Yield();
            var success = manager.TryRespondToRematch(original.RoomId, bob.PlayerId, true,
                Now.AddMinutes(1).AddSeconds(1), out _, out var room, out _);
            return (success, room);
        }

        var results = await Task.WhenAll(Task.Run(AcceptAsync), Task.Run(AcceptAsync));
        Assert.Single(results.Where(result => result.Success));
        Assert.Single(results.Where(result => result.Room is not null));
        Assert.Single(manager.GetRoomsSnapshot(activeOnly: true));
    }

    [Fact]
    public void RematchRequesterCanCancelAndImmediatelyReleasesBothPlayers()
    {
        var (players, manager) = CreateLobby();
        var alice = Login(players, "Alice", "conn-1");
        var bob = Login(players, "Bob", "conn-2");
        var challenge = manager.SendChallenge(alice.PlayerId, bob.PlayerId, "10+0", Now, TimeSpan.FromSeconds(30));
        var original = manager.AcceptChallenge(challenge.Challenge!.ChallengeId, bob.PlayerId, Now.AddSeconds(1)).Room!;
        Assert.True(original.TryFinish(new GameResult("BLACK_WIN", "TIMEOUT", SideColor.Black, Now.AddMinutes(1), 3, "Timeout")));
        players.LeaveRoom(alice.PlayerId);
        players.LeaveRoom(bob.PlayerId);
        Assert.True(manager.TryRequestRematch(original.RoomId, alice.PlayerId, Now.AddMinutes(1),
            TimeSpan.FromSeconds(60), out _, out _));

        Assert.False(manager.TryCancelRematch(original.RoomId, bob.PlayerId, out _, out _));
        Assert.True(manager.TryCancelRematch(original.RoomId, alice.PlayerId, out _, out _));
        Assert.Equal(PlayerStatus.AVAILABLE, alice.Status);
        Assert.Equal(PlayerStatus.AVAILABLE, bob.Status);
        Assert.Null(alice.ActiveChallengeId);
        Assert.Null(bob.ActiveChallengeId);
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
