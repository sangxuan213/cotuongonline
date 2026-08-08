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
        if (_transport is not null) _transport.DisposeAsync().AsTask().GetAwaiter().GetResult();
        base.OnExit(e);
    }
}

