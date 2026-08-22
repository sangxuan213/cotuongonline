using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using XiangqiOnline.Persistence.Configuration;
using XiangqiOnline.Persistence.Services;
using XiangqiOnline.Server;
using XiangqiOnline.Server.Lobby;
using XiangqiOnline.Server.Networking;
using XiangqiOnline.Server.Accounts;

var configurationBuilder = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false);
if (Environment.GetEnvironmentVariable("XIANGQI_EMAIL_CONFIG") is { Length: > 0 } localEmailConfig)
    configurationBuilder.AddJsonFile(Path.GetFullPath(localEmailConfig), optional: true, reloadOnChange: false);
var configuration = configurationBuilder.Build();

var options = configuration.GetSection("Server").Get<ServerOptions>() ?? new ServerOptions();
if (Environment.GetEnvironmentVariable("XIANGQI_BIND_ADDRESS") is { Length: > 0 } bindAddress)
    options.BindAddress = bindAddress.Trim();
var hostingPort = Environment.GetEnvironmentVariable("XIANGQI_SERVER_PORT") ?? Environment.GetEnvironmentVariable("PORT");
if (int.TryParse(hostingPort, out var parsedPort)) options.Port = parsedPort;
options.Validate();

ServerConsoleLog.Initialize(options.BindAddress, options.Port);
ServerConsoleLog.Info("KHỞI ĐỘNG", "Đang nạp cấu hình, luật chơi và cơ sở dữ liệu...");

MessageRouter router = new();
router.Register("HELLO", HelloMessageHandler.HandleAsync);

var players = new PlayerSessionDirectory(reconnectWindow: TimeSpan.FromSeconds(options.ReconnectWindowSeconds));
var challenges = new ChallengeManager(players);
var persistence = new GamePersistenceService(DatabaseOptions.FromEnvironment(), NullLoggerFactory.Instance);
persistence.InitializeDatabase();
var resetPepper = Environment.GetEnvironmentVariable("XIANGQI_RESET_PEPPER") ?? "UDM18-local-reset-pepper-change-on-hosting";
var accountService = new AccountService(DatabaseOptions.FromEnvironment(), resetPepper);
var emailOptions = EmailOptions.FromConfiguration(configuration);
var accountHandler = new AccountMessageHandler(accountService, new SmtpPasswordResetEmailSender(emailOptions), players);
players.PlayerListUpdated += update =>
{
    var changed = update.Players.FirstOrDefault(player => player.PlayerId == update.ChangedPlayerId);
    var identity = changed is null ? update.ChangedPlayerId : $"{changed.DisplayName} ({changed.Status})";
    ServerConsoleLog.Info("SẢNH", $"{identity} • {update.Reason} • tổng {update.Players.Count} người chơi");
};

GameServerHost host;
try
{
    host = new GameServerHost(
        options.BindAddress,
        options.Port,
        router,
        players,
        challenges,
        options.RequestsPerSecond,
        TimeSpan.FromSeconds(options.HeartbeatTimeoutSeconds));
    LobbyMessageRoutes.Register(router, players, challenges, host);
    LobbyMessageRoutes.RegisterAccounts(router, accountHandler);
    var bots = new BotMoveService(players, host, persistence);
    MoveMessageRoutes.Register(router, players, challenges, host, persistence, bots);
    PhaseRoutes.Register(router, players, challenges, host, persistence, bots);
}
catch (Exception ex)
{
    // Bad IP/port in config must not crash — print a clear message and exit cleanly.
    ServerConsoleLog.Error("CẤU HÌNH", $"IP/Port không hợp lệ: {ex.Message}");
    return 1;
}

host.ConnectionOpened += id =>
{
    ServerConsoleLog.Success("KẾT NỐI", $"Máy khách #{id} đã kết nối");
};
host.ConnectionClosed += id =>
{
    ServerConsoleLog.Warning("NGẮT", $"Máy khách #{id} đã đóng kết nối");
};

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

try
{
    await host.StartAsync(cts.Token);
    ServerConsoleLog.Success("SẴN SÀNG", $"Đang lắng nghe tại {options.BindAddress}:{options.Port} • Ctrl+C để dừng");
}
catch (InvalidOperationException ex)
{
    ServerConsoleLog.Error("MẠNG", $"Không thể lắng nghe: {ex.Message}");
    return 1;
}

var lifecycle = new GameLifecycleMonitor(challenges, players, host, persistence);
var lifecycleTask = lifecycle.RunAsync(cts.Token);

try
{
    await Task.Delay(Timeout.Infinite, cts.Token);
}
catch (OperationCanceledException)
{
    // Ctrl+C — normal shutdown path
}

ServerConsoleLog.Warning("HỆ THỐNG", "Đang dừng máy chủ...");
await host.StopAsync();
try { await lifecycleTask; } catch (OperationCanceledException) { }
ServerConsoleLog.Info("HỆ THỐNG", "Máy chủ đã dừng an toàn.");
return 0;
