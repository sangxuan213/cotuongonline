using System.IO;
using UDM18.Client.Protocol;

namespace UDM18.Client.ViewModels;

public sealed class ConnectionViewModel : ObservableObject
{
    private readonly GameClient _client;
    private string _host = "127.0.0.1";
    private int _port = 18180;
    private string _displayName = "";
    private string _status = "ChĂ†Â°a kĂ¡ÂºÂ¿t nĂ¡Â»â€˜i";
    private string? _error;
    private bool _isConnected;

    public ConnectionViewModel(GameClient client)
    {
        _client = client;
        ConnectCommand = new AsyncRelayCommand(ConnectAsync, CanConnect);
        DisconnectCommand = new AsyncRelayCommand(_ => _client.DisconnectAsync(), () => IsConnected);
        ConnectCommand.Failed += ex => Error = ex.Message;
        DisconnectCommand.Failed += ex => Error = ex.Message;
        _client.ConnectionChanged += OnConnectionChanged;
        _client.LoginCompleted += (_, name) => Ui(() => Status = $"Ă„ÂÄ‚Â£ Ă„â€˜Ă„Æ’ng nhĂ¡ÂºÂ­p: {name}");
        _client.ErrorReceived += message => Ui(() => Error = message);
    }

    public string Host { get => _host; set { if (Set(ref _host, value)) ConnectCommand.NotifyCanExecuteChanged(); } }
    public int Port { get => _port; set { if (Set(ref _port, value)) ConnectCommand.NotifyCanExecuteChanged(); } }
    public string DisplayName { get => _displayName; set { if (Set(ref _displayName, value)) ConnectCommand.NotifyCanExecuteChanged(); } }
    public string Status { get => _status; private set => Set(ref _status, value); }
    public string? Error { get => _error; private set => Set(ref _error, value); }
    public bool IsConnected { get => _isConnected; private set { if (Set(ref _isConnected, value)) { ConnectCommand.NotifyCanExecuteChanged(); DisconnectCommand.NotifyCanExecuteChanged(); } } }
    public AsyncRelayCommand ConnectCommand { get; }
    public AsyncRelayCommand DisconnectCommand { get; }

    private bool CanConnect() => !IsConnected && !string.IsNullOrWhiteSpace(Host) && Port is > 0 and <= 65535 && DisplayName.Trim().Length is >= 1 and <= 24;

    private async Task ConnectAsync(CancellationToken cancellationToken)
    {
        Error = null;
        try { await _client.ConnectAndLoginAsync(Host.Trim(), Port, DisplayName.Trim(), cancellationToken); }
        catch (Exception ex) when (ex is IOException or System.Net.Sockets.SocketException or OperationCanceledException)
        { Error = ex is OperationCanceledException ? "Ă„ÂÄ‚Â£ hĂ¡Â»Â§y kĂ¡ÂºÂ¿t nĂ¡Â»â€˜i." : ex.Message; }
    }

    private void OnConnectionChanged(ConnectionState state, string? error) => Ui(() =>
    {
        IsConnected = state == ConnectionState.Connected;
        Status = state switch
        {
            ConnectionState.Connecting => "Ă„Âang kĂ¡ÂºÂ¿t nĂ¡Â»â€˜i...",
            ConnectionState.Connected => "Ă„ÂÄ‚Â£ kĂ¡ÂºÂ¿t nĂ¡Â»â€˜i, Ă„â€˜ang Ă„â€˜Ă„Æ’ng nhĂ¡ÂºÂ­p...",
            ConnectionState.Failed => "KĂ¡ÂºÂ¿t nĂ¡Â»â€˜i thĂ¡ÂºÂ¥t bĂ¡ÂºÂ¡i",
            _ => "ChĂ†Â°a kĂ¡ÂºÂ¿t nĂ¡Â»â€˜i"
        };
        if (!string.IsNullOrWhiteSpace(error)) Error = error;
    });

    private static void Ui(Action action)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess()) action(); else dispatcher.Invoke(action);
    }
}
