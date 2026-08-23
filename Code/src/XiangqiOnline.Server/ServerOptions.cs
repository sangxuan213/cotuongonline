namespace XiangqiOnline.Server
{
    /// <summary>Bound from the "Server" section of appsettings.json. See §10.1: "Server bind cấu hình".</summary>
    public class ServerOptions
    {
        public string BindAddress { get; set; } = "0.0.0.0";
        public int Port { get; set; } = 5000;
        public int HeartbeatIntervalSeconds { get; set; } = 10;
        public int HeartbeatTimeoutSeconds { get; set; } = 30;
        public int ReconnectWindowSeconds { get; set; } = 60;
        public int RequestsPerSecond { get; set; } = 40;

        public void Validate()
        {
            if (string.IsNullOrWhiteSpace(BindAddress)) throw new InvalidOperationException("Server bind address is required.");
            if (Port is < 0 or > 65535) throw new InvalidOperationException("Server port must be between 0 and 65535.");
            if (HeartbeatIntervalSeconds <= 0 || HeartbeatTimeoutSeconds <= HeartbeatIntervalSeconds)
                throw new InvalidOperationException("Heartbeat timeout must be greater than its interval.");
            if (ReconnectWindowSeconds <= 0) throw new InvalidOperationException("Reconnect window must be positive.");
            if (RequestsPerSecond <= 0) throw new InvalidOperationException("Request rate limit must be positive.");
        }
    }
}
