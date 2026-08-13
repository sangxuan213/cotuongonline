using XiangqiOnline.Server.Lobby;
using XiangqiOnline.Shared.Enums;
using XiangqiOnline.Shared.Models;

namespace XiangqiOnline.Server.Tests.Lobby;

public sealed class GameRoomTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 9, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void RoomRequiresTwoDifferentPlayers()
    {
        Assert.Throws<ArgumentException>(() =>
            new GameRoom("room-1", "p1", "p1", "UDM18_WXF_PRO_2018", "COURSE_DEMO", Now));
    }

    [Fact]
    public void NewRoomStartsWithRedTurnAndZeroRevision()
    {
        var room = NewRoom();

        Assert.Equal(SideColor.Red, room.CurrentTurn);
        Assert.Equal(0, room.Revision);
        Assert.Equal(GameRoomStatus.CREATED, room.Status);
    }

    [Fact]
    public void StartMovesRoomToPlaying()
    {
        var room = NewRoom();

        room.MarkWaitingForReady();
        room.Start();

        Assert.Equal(GameRoomStatus.PLAYING, room.Status);
        Assert.Equal(SideColor.Red, room.CurrentTurn);
    }

    [Fact]
    public void CommitRevisionOnlyWorksWhilePlaying()
    {
        var room = NewRoom();

        Assert.Throws<InvalidOperationException>(() => room.CommitRevision(room.Board.ApplyMove(new Position(0, 6), new Position(0, 5))));
        Assert.Equal(SideColor.Red, room.Board.Turn);

        room.Start();
        var revision = room.CommitRevision(room.Board.ApplyMove(new Position(0, 6), new Position(0, 5)));

        Assert.Equal(1, revision);
        Assert.Equal(SideColor.Black, room.CurrentTurn);
        Assert.Equal(SideColor.Black, room.Board.Turn);
    }

    [Fact]
    public void TerminalRoomCannotTransitionAgain()
    {
        var room = NewRoom();
        room.Finish();

        Assert.True(room.IsTerminal);
        Assert.Throws<InvalidOperationException>(() => room.AbortSystem());
    }

    private static GameRoom NewRoom() =>
        new("room-1", "red", "black", "UDM18_WXF_PRO_2018", "COURSE_DEMO", Now);
}
