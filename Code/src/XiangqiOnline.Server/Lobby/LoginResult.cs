namespace XiangqiOnline.Server.Lobby;

public sealed record LoginResult(
    bool IsSuccess,
    PlayerSession? Session,
    string? ErrorCode,
    string Message)
{
    public static LoginResult Success(PlayerSession session) =>
        new(true, session, null, "Login accepted.");

    public static LoginResult Fail(string errorCode, string message) =>
        new(false, null, errorCode, message);
}
