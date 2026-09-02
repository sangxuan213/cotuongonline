using System.IO;
using System.Net.Mail;
using UDM18.Client.Protocol;

namespace UDM18.Client.ViewModels;

public sealed class ConnectionViewModel : ObservableObject
{
    private readonly GameClient _client;
    private string _host = DefaultHost(), _portText = DefaultPort(), _displayName = "", _email = "", _password = "", _confirmPassword = "", _resetCode = "", _newPassword = "";
    private string _status = "Chưa kết nối";
    private string? _error, _notice;
    private bool _isConnected;

    public event Action? LoginSucceeded;
    public event Action? ReconnectSucceeded;

    public ConnectionViewModel(GameClient client)
    {
        _client = client;
        ConnectCommand = Command(ConnectGuestAsync);
        AccountLoginCommand = Command(LoginAccountAsync);
        RegisterCommand = Command(RegisterAsync);
        RequestResetCommand = Command(RequestResetAsync);
        ConfirmResetCommand = Command(ConfirmResetAsync);
        DisconnectCommand = Command(_ => _client.DisconnectAsync());
        ReconnectCommand = Command(ReconnectAsync);
        _client.ConnectionChanged += OnConnectionChanged;
        _client.AccountNotice += message => Ui(() => Notice = message);
        _client.Reconnected += () => Ui(() => { Status = "Đã kết nối lại vào ván đang chơi."; ReconnectSucceeded?.Invoke(); });
        _client.LoginCompleted += (_, name) => Ui(() => { Status = $"Đã đăng nhập: {name}"; LoginSucceeded?.Invoke(); });
        _client.ErrorReceived += message => Ui(() => Error = message);
    }

    public string Host { get => _host; set => Set(ref _host, value); }
    public string PortText { get => _portText; set => Set(ref _portText, value); }
    public string DisplayName { get => _displayName; set => Set(ref _displayName, value); }
    public string Email { get => _email; set => Set(ref _email, value); }
    public string Password { get => _password; set => Set(ref _password, value); }
    public string ConfirmPassword { get => _confirmPassword; set => Set(ref _confirmPassword, value); }
    public string ResetCode { get => _resetCode; set => Set(ref _resetCode, value); }
    public string NewPassword { get => _newPassword; set => Set(ref _newPassword, value); }
    public string Status { get => _status; private set => Set(ref _status, value); }
    public string? Error { get => _error; private set => Set(ref _error, value); }
    public string? Notice { get => _notice; private set => Set(ref _notice, value); }
    public bool IsConnected { get => _isConnected; private set => Set(ref _isConnected, value); }

    public AsyncRelayCommand ConnectCommand { get; }
    public AsyncRelayCommand AccountLoginCommand { get; }
    public AsyncRelayCommand RegisterCommand { get; }
    public AsyncRelayCommand RequestResetCommand { get; }
    public AsyncRelayCommand ConfirmResetCommand { get; }
    public AsyncRelayCommand DisconnectCommand { get; }
    public AsyncRelayCommand ReconnectCommand { get; }

    private AsyncRelayCommand Command(Func<CancellationToken, Task> action)
    {
        var command = new AsyncRelayCommand(action);
        command.Failed += ex => Ui(() => Error = ex.Message);
        return command;
    }

    private async Task ConnectGuestAsync(CancellationToken ct)
    {
        Begin();
        if (!Endpoint(out var port) || DisplayName.Trim().Length is < 1 or > 24) { Error = "Kiểm tra máy chủ, cổng và tên hiển thị (1–24 ký tự)."; return; }
        await RunNetwork(() => _client.ConnectAndLoginAsync(Host.Trim(), port, DisplayName.Trim(), ct));
    }

    private async Task LoginAccountAsync(CancellationToken ct)
    {
        Begin();
        if (!Endpoint(out var port) || !ValidEmail()) { Error = "Email hoặc địa chỉ máy chủ không hợp lệ."; return; }
        if (string.IsNullOrEmpty(Password)) { Error = "Hãy nhập mật khẩu."; return; }
        await RunNetwork(() => _client.ConnectAndAccountLoginAsync(Host.Trim(), port, Email.Trim(), Password, ct));
    }

    private async Task RegisterAsync(CancellationToken ct)
    {
        Begin();
        if (!Endpoint(out var port) || !ValidEmail()) { Error = "Email hoặc địa chỉ máy chủ không hợp lệ."; return; }
        if (DisplayName.Trim().Length is < 1 or > 24) { Error = "Tên hiển thị cần từ 1 đến 24 ký tự."; return; }
        if (!Strong(Password)) { Error = "Mật khẩu cần từ 8 đến 128 ký tự."; return; }
        if (Password != ConfirmPassword) { Error = "Hai mật khẩu chưa trùng nhau."; return; }
        await RunNetwork(() => _client.ConnectAndRegisterAsync(Host.Trim(), port, Email.Trim(), DisplayName.Trim(), Password, ct));
    }

    private async Task RequestResetAsync(CancellationToken ct)
    {
        Begin();
        if (!Endpoint(out var port) || !ValidEmail()) { Error = "Hãy nhập email và địa chỉ máy chủ hợp lệ."; return; }
        Notice = await _client.RequestPasswordResetAsync(Host.Trim(), port, Email.Trim(), ct);
    }

    private async Task ConfirmResetAsync(CancellationToken ct)
    {
        Begin();
        if (!Endpoint(out var port) || !ValidEmail() || ResetCode.Trim().Length != 6) { Error = "Email hoặc mã xác nhận 6 số không hợp lệ."; return; }
        if (!Strong(NewPassword)) { Error = "Mật khẩu mới cần từ 8 đến 128 ký tự."; return; }
        Notice = await _client.ConfirmPasswordResetAsync(Host.Trim(), port, Email.Trim(), ResetCode.Trim(), NewPassword, ct);
        Password = NewPassword; NewPassword = ""; ResetCode = "";
    }

    private Task ReconnectAsync(CancellationToken ct) => Endpoint(out var port) && _client.ResumeToken is { } token
        ? _client.ReconnectAsync(Host.Trim(), port, token, ct) : Task.CompletedTask;
    private async Task RunNetwork(Func<Task> action) { try { await action(); } catch (Exception ex) when (ex is IOException or System.Net.Sockets.SocketException or OperationCanceledException) { Error = ex is OperationCanceledException ? "Đã hủy kết nối." : ex.Message; } }
    private void Begin() { Error = null; Notice = null; Status = "Đang kiểm tra thông tin..."; }
    private bool Endpoint(out int port)
    {
        port = 0;
        return !string.IsNullOrWhiteSpace(Host) && int.TryParse(PortText, out port) && port is > 0 and <= 65535;
    }
    private static string DefaultHost()
    {
        if (Environment.GetEnvironmentVariable("XIANGQI_SERVER_HOST")?.Trim() is { Length: > 0 } host) return host;
        return ReadPackagedEndpoint().Host ?? "127.0.0.1";
    }

    private static string DefaultPort()
    {
        if (int.TryParse(Environment.GetEnvironmentVariable("XIANGQI_SERVER_PORT"), out var port) && port is > 0 and <= 65535)
            return port.ToString();
        return ReadPackagedEndpoint().Port?.ToString() ?? "5000";
    }

    private static (string? Host, int? Port) ReadPackagedEndpoint()
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "server.endpoint");
            if (!File.Exists(path)) return (null, null);
            var value = File.ReadAllText(path).Trim();
            var separator = value.LastIndexOf(':');
            if (separator <= 0 || separator == value.Length - 1) return (null, null);
            var host = value[..separator].Trim();
            return int.TryParse(value[(separator + 1)..], out var port) && port is > 0 and <= 65535
                ? (host, port)
                : (null, null);
        }
        catch (IOException) { return (null, null); }
        catch (UnauthorizedAccessException) { return (null, null); }
    }
    private bool ValidEmail()
    {
        try
        {
            return new MailAddress(Email.Trim()).Address.Equals(Email.Trim(), StringComparison.OrdinalIgnoreCase);
        }
        catch (FormatException)
        {
            return false;
        }
    }
    private static bool Strong(string value) => value.Length is >= 8 and <= 128 && value.Any(character => !char.IsWhiteSpace(character));

    private void OnConnectionChanged(ConnectionState state, string? error) => Ui(() =>
    {
        IsConnected = state == ConnectionState.Connected;
        Status = state switch { ConnectionState.Connecting => "Đang kết nối an toàn...", ConnectionState.Connected => "Đã kết nối máy chủ", ConnectionState.Failed => "Kết nối thất bại", _ => "Chưa kết nối" };
        if (!string.IsNullOrWhiteSpace(error)) Error = error;
    });
    private static void Ui(Action action) { var dispatcher = System.Windows.Application.Current?.Dispatcher; if (dispatcher is null || dispatcher.CheckAccess()) action(); else dispatcher.Invoke(action); }
}
