using XiangqiOnline.Shared.Models;

namespace XiangqiOnline.Server.Lobby;

public sealed record ChallengeActionResult(
    bool IsSuccess,
    Challenge? Challenge,
    GameRoom? Room,
    string? ErrorCode,
    string Message)
{
    public static ChallengeActionResult Sent(Challenge challenge) =>
        new(true, challenge, null, null, "Challenge sent.");

    public static ChallengeActionResult Rejected(Challenge challenge) =>
        new(true, challenge, null, null, "Challenge rejected.");

    public static ChallengeActionResult Accepted(Challenge challenge, GameRoom room) =>
        new(true, challenge, room, null, "Challenge accepted.");

    public static ChallengeActionResult Cancelled(Challenge challenge) =>
        new(true, challenge, null, null, "Challenge cancelled.");

    public static ChallengeActionResult Fail(string errorCode, string message) =>
        new(false, null, null, errorCode, message);
}
