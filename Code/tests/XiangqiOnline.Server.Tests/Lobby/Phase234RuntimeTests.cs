using XiangqiOnline.Server.Lobby;
using XiangqiOnline.Shared.Enums;
using XiangqiOnline.RuleEngine.Models;
using XiangqiOnline.RuleEngine.Pipeline;

namespace XiangqiOnline.Server.Tests.Lobby;

public sealed class Phase234RuntimeTests
{
    [Theory]
    [InlineData(BotDifficulty.Easy)]
    [InlineData(BotDifficulty.Medium)]
    [InlineData(BotDifficulty.Hard)]
    public void BotDifficulty_AlwaysChoosesALegalMove(BotDifficulty difficulty)
    {
        var board = BoardState.CreateInitialBoard(SideColor.Black);
        var move = BotMoveService.ChooseMove(board, 0, difficulty);
        Assert.NotNull(move);
        Assert.True(new MoveValidationPipeline().Validate(board, move!).IsValid);
    }

    [Fact]
    public void BotRoom_SeatsHumanAsRedAndBotAsBlack()
    {
        var players = new PlayerSessionDirectory(() => "human");
        Assert.True(players.Login("Người chơi", "connection", DateTimeOffset.UtcNow).IsSuccess);
        var challenges = new ChallengeManager(players, roomIdFactory: () => "bot-room");
        Assert.True(challenges.TryCreateBotRoom("human", "HARD", DateTimeOffset.UtcNow, out var room, out _));
        Assert.Equal("human", room.RedPlayerId);
        Assert.StartsWith("BOT_HARD_", room.BlackPlayerId);
        Assert.Equal(PlayerStatus.IN_GAME, players.GetSnapshot().Single().Status);
        Assert.Equal(TimeSpan.Zero, room.Clock.Profile.Increment);
    }

    [Fact]
    public void PublicRoom_CreateAndJoin_StartsARealTwoPlayerGame()
    {
        var ids = new Queue<string>(["owner", "guest"]);
        var players = new PlayerSessionDirectory(() => ids.Dequeue());
        Assert.True(players.Login("Chủ phòng", "connection-owner", DateTimeOffset.UtcNow).IsSuccess);
        Assert.True(players.Login("Khách", "connection-guest", DateTimeOffset.UtcNow).IsSuccess);
        var challenges = new ChallengeManager(players, roomIdFactory: () => "public-room");

        Assert.True(challenges.TryCreateWaitingRoom("owner", DateTimeOffset.UtcNow, out var waiting, out _));
        Assert.Equal("public-room", waiting.RoomId);
        Assert.Single(challenges.GetWaitingRoomsSnapshot());
        Assert.Equal(PlayerStatus.INVITING, players.GetSnapshot().Single(player => player.PlayerId == "owner").Status);

        Assert.True(challenges.TryJoinWaitingRoom("public-room", "guest", DateTimeOffset.UtcNow, out var room, out _));
        Assert.Equal(GameRoomStatus.PLAYING, room.Status);
        Assert.Equal("owner", room.RedPlayerId);
        Assert.Equal("guest", room.BlackPlayerId);
        Assert.Equal(TimeSpan.Zero, room.Clock.Profile.Increment);
        Assert.Empty(challenges.GetWaitingRoomsSnapshot());
        Assert.All(players.GetSnapshot(), player => Assert.Equal(PlayerStatus.IN_GAME, player.Status));
    }

    [Fact]
    public void PublicRoom_OwnerCannotJoinTheirOwnRoom()
    {
        var players = new PlayerSessionDirectory(() => "owner");
        Assert.True(players.Login("Chủ phòng", "connection", DateTimeOffset.UtcNow).IsSuccess);
        var challenges = new ChallengeManager(players, roomIdFactory: () => "public-room");
        Assert.True(challenges.TryCreateWaitingRoom("owner", DateTimeOffset.UtcNow, out _, out _));

        Assert.False(challenges.TryJoinWaitingRoom("public-room", "owner", DateTimeOffset.UtcNow, out _, out var error));
        Assert.Contains("owner", error, StringComparison.OrdinalIgnoreCase);
        Assert.Single(challenges.GetWaitingRoomsSnapshot());
    }

    [Fact]
    public void LockedRoom_RequiresCorrectPasswordWithoutLeakingIt()
    {
        var ids = new Queue<string>(["owner", "guest"]);
        var players = new PlayerSessionDirectory(() => ids.Dequeue());
        Assert.True(players.Login("Chủ phòng", "connection-owner", DateTimeOffset.UtcNow).IsSuccess);
        Assert.True(players.Login("Khách", "connection-guest", DateTimeOffset.UtcNow).IsSuccess);
        var challenges = new ChallengeManager(players, roomIdFactory: () => "locked-room");

        Assert.True(challenges.TryCreateWaitingRoom("owner", "mat-khau-123", DateTimeOffset.UtcNow, out var waiting, out _));
        Assert.True(waiting.IsLocked);
        Assert.NotEqual("mat-khau-123", waiting.PasswordHash);
        Assert.False(challenges.TryJoinWaitingRoom("locked-room", "guest", "sai", DateTimeOffset.UtcNow, out _, out var wrongError));
        Assert.Contains("Mật khẩu", wrongError, StringComparison.OrdinalIgnoreCase);
        Assert.True(challenges.TryJoinWaitingRoom("locked-room", "guest", "mat-khau-123", DateTimeOffset.UtcNow, out var room, out _));
        Assert.Equal(GameRoomStatus.PLAYING, room.Status);
    }

    [Fact]
    public void SessionToken_Has256BitEntropyAndVerifiesInConstantTimePath()
    {
        var service = new SessionTokenService();
        var issued = service.Issue();

        Assert.True(issued.PlainText.Length >= 43);
        Assert.Equal(64, issued.Hash.Length);
        Assert.True(service.Verify(issued.PlainText, issued.Hash));
        Assert.False(service.Verify(issued.PlainText + "x", issued.Hash));
    }

    [Fact]
    public void InGameDisconnect_CanResumeBySecureTokenWithinWindow()
    {
        var now = new DateTimeOffset(2026, 8, 15, 0, 0, 0, TimeSpan.Zero);
        var directory = new PlayerSessionDirectory(() => "player-1");
        var login = directory.Login("Người chơi", "connection-1", now);
        Assert.True(login.IsSuccess);
        Assert.NotNull(login.SessionToken);
        directory.EnterRoom("player-1", "room-1");

        directory.MarkOfflineByConnectionId("connection-1", now.AddSeconds(1));
        var resumed = directory.ResumeByToken(login.SessionToken!, "connection-2", now.AddSeconds(20));

        Assert.True(resumed.IsSuccess);
        Assert.Equal("connection-2", resumed.Session!.ConnectionId);
        Assert.Equal(PlayerStatus.IN_GAME, resumed.Session.Status);
        Assert.Equal(PlayerSessionConnectionState.CONNECTED, resumed.Session.ConnectionState);
    }

    [Fact]
    public void ServerClock_UsesMonotonicTimeAndAddsIncrementOnlyForLegalCommit()
    {
        var time = new ManualTimeProvider();
        var clock = new ServerClock(new TimeProfileSpec("test", TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(2)),
            SideColor.Red, time);
        time.Advance(TimeSpan.FromSeconds(3));

        Assert.True(clock.TryCommitMove(SideColor.Red, out var snapshot));
        Assert.InRange(snapshot.RedRemainingMs, 8999, 9001);
        Assert.Equal(SideColor.Black, snapshot.ActiveSide);
        Assert.False(clock.TryCommitMove(SideColor.Red, out _));
    }

    [Fact]
    public void GameRoom_ResultIsExactlyOnceAndSpectatorsAreReadOnlyMembership()
    {
        var room = new GameRoom("room", "red", "black", "UDM18_WXF_PRO_2018", "60+30", DateTimeOffset.UtcNow);
        room.Start();
        Assert.True(room.AddSpectator("spectator-connection"));
        Assert.False(room.AddSpectator("spectator-connection"));

        var result = new GameResult("RED_WIN", "RESIGNATION", SideColor.Red, DateTimeOffset.UtcNow, 0, "Black resigned.");
        Assert.True(room.TryFinish(result));
        Assert.False(room.TryFinish(result));
        Assert.True(room.IsTerminal);
        Assert.True(room.Clock.IsStopped);
        Assert.True(room.TryMarkGameEndedBroadcasted());
        Assert.False(room.TryMarkGameEndedBroadcasted());
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private long _ticks;
        private DateTimeOffset _utcNow = DateTimeOffset.UnixEpoch;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => _ticks;
        public override DateTimeOffset GetUtcNow() => _utcNow;
        public void Advance(TimeSpan amount)
        {
            _ticks += amount.Ticks;
            _utcNow += amount;
        }
    }
}
