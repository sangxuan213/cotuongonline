namespace XiangqiOnline.Shared.Protocol
{
    /// <summary>
    /// Type string dùng trong RequestEnvelope/ServerEventEnvelope cho khung heartbeat.
    /// Cả 2 chiều (Client->Server và Server->Client) đều có thể gửi PING — "heartbeat
    /// hai chiều" theo yêu cầu Ngày 1: bên nào rảnh quá HeartbeatIntervalMs thì tự gửi
    /// PING của mình, không chờ bên kia hỏi trước.
    /// </summary>
    public static class HeartbeatMessageTypes
    {
        public const string Ping = "PING";
        public const string Pong = "PONG";
    }
}
