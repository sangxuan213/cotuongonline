using XiangqiOnline.Server.Lobby;

namespace XiangqiOnline.Server.Tests.Lobby;

public sealed class ChallengeTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 9, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ConstructorRejectsSelfChallenge()
    {
        Assert.Throws<ArgumentException>(() =>
            new Challenge("c1", "p1", "p1", "COURSE_DEMO", Now, Now.AddSeconds(30)));
    }

    [Fact]
    public void AcceptMovesPendingChallengeToAccepted()
    {
        var challenge = NewChallenge();

        challenge.Accept("target", Now.AddSeconds(1));

        Assert.Equal(ChallengeStatus.ACCEPTED, challenge.Status);
    }

    [Fact]
    public void OnlyTargetCanAcceptChallenge()
    {
        var challenge = NewChallenge();

        Assert.Throws<InvalidOperationException>(() => challenge.Accept("challenger", Now.AddSeconds(1)));
    }

    [Fact]
    public void TerminalChallengeCannotTransitionAgain()
    {
        var challenge = NewChallenge();
        challenge.Reject("target");

        Assert.Throws<InvalidOperationException>(() => challenge.Accept("target", Now.AddSeconds(1)));
    }

    [Fact]
    public void ExpireOnlyChangesPendingChallengeAfterDeadline()
    {
        var challenge = NewChallenge();

        Assert.False(challenge.Expire(Now.AddSeconds(29)));
        Assert.True(challenge.Expire(Now.AddSeconds(30)));
        Assert.Equal(ChallengeStatus.EXPIRED, challenge.Status);
    }

    private static Challenge NewChallenge() =>
        new("c1", "challenger", "target", "COURSE_DEMO", Now, Now.AddSeconds(30));
}
