using System.Windows;
using UDM18.Client.Protocol;
using UDM18.Client.ViewModels;

namespace UDM18.Client;

public partial class App : Application
{
    private IProtocolTransport? _transport;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        // WPF tự chọn GPU và tự fallback khi máy không hỗ trợ. Ép SoftwareOnly làm
        // banner, blur và animation toàn màn hình chạy trên CPU nên gây giật rõ rệt.
        // Chỉ bật lại chế độ này để chẩn đoán bằng XIANGQI_SOFTWARE_RENDERING=1.
        if (string.Equals(Environment.GetEnvironmentVariable("XIANGQI_SOFTWARE_RENDERING"), "1", StringComparison.Ordinal))
            System.Windows.Media.RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;
        _transport = new TcpProtocolTransport();
        var client = new GameClient(_transport);
        var demoMode = e.Args.Any(arg => arg.Equals("--demo", StringComparison.OrdinalIgnoreCase));
        var connection = new ConnectionViewModel(client);
        var lobby = new LobbyViewModel(client);
        var gameRoom = new GameRoomViewModel(client);
        if (demoMode)
        {
            lobby.LoadDemoData();
            gameRoom.LoadDemoData();
        }
        var shell = new ShellViewModel(connection, lobby, gameRoom, demoMode);
        new MainWindow { DataContext = shell }.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _transport?.Abort();
        base.OnExit(e);
    }
}
