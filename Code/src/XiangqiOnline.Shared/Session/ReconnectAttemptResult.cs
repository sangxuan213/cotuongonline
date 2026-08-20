namespace XiangqiOnline.Shared.Session
{
    /// <summary>Kết quả 1 lần gọi <see cref="ReconnectSessionTracker.TryReconnect"/> — map trực tiếp ra RECONNECT_ACCEPTED/REJECTED.</summary>
    public readonly record struct ReconnectAttemptResult(bool IsAccepted, string? PlayerId, string? ErrorCode)
    {
        public static ReconnectAttemptResult Accepted(string playerId) => new(true, playerId, null);
        public static ReconnectAttemptResult Rejected(string errorCode) => new(false, null, errorCode);
    }
}
