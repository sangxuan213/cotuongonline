namespace XiangqiOnline.Shared.Session
{
    /// <summary>Trạng thái "còn kết nối hay đang chờ reconnect" của 1 playerId.</summary>
    public enum ConnectionPresenceState
    {
        /// <summary>Không có bản ghi nào cho playerId này (chưa từng MarkConnected).</summary>
        Unknown,

        /// <summary>Đang có 1 connectionId sống gắn với playerId này.</summary>
        Connected,

        /// <summary>Socket vừa chết — còn trong cửa sổ reconnect, session CHƯA bị xoá.</summary>
        AwaitingReconnect,

        /// <summary>Quá deadline reconnect — coi như phiên đã kết thúc thật sự.</summary>
        Expired
    }
}
