namespace XiangqiOnline.Server.Lobby;

public sealed record LoginResult(
    bool IsSuccess,
    PlayerSession? Session,
    string? ErrorCode,
    string Message,
    string? SessionToken = null)
{
    public static LoginResult Success(PlayerSession session, string? sessionToken = null) =>
        new(true, session, null, "Login accepted.", sessionToken);

    public static LoginResult Fail(string errorCode, string message) =>
        new(false, null, errorCode, message, null);
}
