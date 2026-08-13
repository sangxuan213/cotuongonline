namespace XiangqiOnline.Server
{
    /// <summary>Bound from the "Server" section of appsettings.json. See §10.1: "Server bind cấu hình".</summary>
    public class ServerOptions
    {
        public string BindAddress { get; set; } = "0.0.0.0";
        public int Port { get; set; } = 5000;
    }
}
