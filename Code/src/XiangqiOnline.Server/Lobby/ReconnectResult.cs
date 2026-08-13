namespace XiangqiOnline.Server.Lobby;

public sealed record ReconnectResult(
    bool IsSuccess,
    PlayerSession? Session,
    string? ErrorCode,
    string Message)
{
    public static ReconnectResult Success(PlayerSession session) =>
        new(true, session, null, "Reconnection accepted.");

    public static ReconnectResult Fail(string errorCode, string message) =>
        new(false, null, errorCode, message);
}