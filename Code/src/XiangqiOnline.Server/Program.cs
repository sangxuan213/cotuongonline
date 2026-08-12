using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using XiangqiOnline.Server;
using XiangqiOnline.Shared.Transport;

var configuration = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
    .Build();

var options = configuration.GetSection("Server").Get<ServerOptions>() ?? new ServerOptions();

Console.WriteLine($"[Server] Đang khởi động, bind {options.BindAddress}:{options.Port} ...");

TcpServerHost host;
try
{
    host = new TcpServerHost(options.BindAddress, options.Port);
}
catch (Exception ex)
{
    // Bad IP/port in config must not crash — print a clear message and exit cleanly.
    Console.Error.WriteLine($"[Server] Cấu hình IP/Port không hợp lệ: {ex.Message}");
    return 1;
}

host.ClientAccepted += client =>
{
    var remote = client.Client.RemoteEndPoint;
    Console.WriteLine($"[Server] Client kết nối từ {remote}.");
};
host.AcceptLoopFaulted += ex =>
{
    Console.Error.WriteLine($"[Server] Accept loop lỗi: {ex.Message}");
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
    Console.WriteLine($"[Server] Đang lắng nghe tại {options.BindAddress}:{options.Port}. Nhấn Ctrl+C để dừng.");
}
catch (InvalidOperationException ex)
{
    Console.Error.WriteLine($"[Server] Không thể lắng nghe: {ex.Message}");
    return 1;
}

try
{
    await Task.Delay(Timeout.Infinite, cts.Token);
}
catch (OperationCanceledException)
{
    // Ctrl+C — normal shutdown path
}

Console.WriteLine("[Server] Đang dừng...");
await host.StopAsync();
Console.WriteLine("[Server] Đã dừng.");
return 0;
