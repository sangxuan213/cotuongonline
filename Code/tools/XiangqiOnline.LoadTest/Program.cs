using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading.Channels;
using XiangqiOnline.Shared.Protocol;

var options = LoadOptions.Parse(args);
Directory.CreateDirectory(options.OutputDirectory);
Console.WriteLine($"UDM18 load: {options.Clients} clients, {options.Games} games, {options.Spectators} spectator joins -> {options.Host}:{options.Port}");

var latencies = new ConcurrentBag<double>();
var errors = new ConcurrentBag<string>();
var clients = new VirtualClient?[options.Clients];
var runTag = Guid.NewGuid().ToString("N")[..8];
var wallClock = Stopwatch.StartNew();
try
{
    await Parallel.ForEachAsync(Enumerable.Range(0, options.Clients), async (index, ct) =>
    {
        try { clients[index] = await VirtualClient.ConnectAsync(index + 1, runTag, options, ct); }
        catch (Exception ex) { errors.Add($"connect-{index + 1}: {ex.GetType().Name}: {ex.Message}"); }
    });

    var available = clients.OfType<VirtualClient>().ToArray();
    var roomIds = new List<string>();
    var moveCommits = 0;
    for (var game = 0; game < Math.Min(options.Games, available.Length / 2); game++)
    {
        try
        {
            var red = available[game * 2];
            var black = available[game * 2 + 1];
            await red.SendAsync("CHALLENGE_SEND", new { targetPlayerId = black.PlayerId, timeProfile = "3+2" });
            var invitation = await black.ReadExpectedAsync("CHALLENGE_RECEIVED");
            var challenge = invitation.GetProperty("payload").GetProperty("challenge");
            var challengeId = challenge.GetProperty("challengeId").GetString()!;
            await black.SendAsync("CHALLENGE_ACCEPT", new { challengeId });
            var redRoom = await red.ReadExpectedAsync("ROOM_CREATED");
            await red.ReadExpectedAsync("GAME_STATE_SNAPSHOT");
            await black.ReadExpectedAsync("ROOM_CREATED");
            await black.ReadExpectedAsync("GAME_STATE_SNAPSHOT");
            var roomId = redRoom.GetProperty("payload").GetProperty("roomId").GetString()!;
            roomIds.Add(roomId);
            await red.SendAsync("MOVE_REQUEST", new
            {
                clientMoveId = Guid.NewGuid().ToString("N"), expectedRevision = 0,
                from = new { x = 0, y = 6 }, to = new { x = 0, y = 5 }
            }, roomId);
            await red.ReadExpectedAsync("MOVE_COMMITTED");
            await black.ReadExpectedAsync("MOVE_COMMITTED");
            moveCommits++;
            await black.SendAsync("MOVE_REQUEST", new
            {
                clientMoveId = Guid.NewGuid().ToString("N"), expectedRevision = 1,
                from = new { x = 0, y = 3 }, to = new { x = 0, y = 4 }
            }, roomId);
            await red.ReadExpectedAsync("MOVE_COMMITTED");
            await black.ReadExpectedAsync("MOVE_COMMITTED");
            moveCommits++;
        }
        catch (Exception ex) { errors.Add($"game-{game + 1}: {ex.GetType().Name}: {ex.Message}"); }
    }

    var spectatorJoins = 0;
    if (roomIds.Count > 1)
    {
        for (var index = 0; index < Math.Min(options.Spectators, available.Length); index++)
        {
            try
            {
                var roomId = roomIds[((index / 2) + 1) % roomIds.Count];
                await available[index].SendAsync("SPECTATOR_JOIN", new { roomId }, roomId);
                await available[index].ReadExpectedAsync("GAME_STATE_SNAPSHOT");
                spectatorJoins++;
            }
            catch (Exception ex) { errors.Add($"spectator-{index + 1}: {ex.GetType().Name}: {ex.Message}"); }
        }
    }

    await Parallel.ForEachAsync(available, async (client, ct) =>
    {
        try { await client.MeasurePingAsync(options.DurationSeconds, options.PingIntervalMs, latencies, ct); }
        catch (Exception ex) { errors.Add($"ping-{client.Index}: {ex.GetType().Name}: {ex.Message}"); }
    });

    wallClock.Stop();
    var ordered = latencies.OrderBy(value => value).ToArray();
    var report = new LoadReport(
        DateTimeOffset.UtcNow, options.Host, options.Port, options.Clients, available.Length,
        roomIds.Count, moveCommits, spectatorJoins, options.DurationSeconds, ordered.Length, errors.Count,
        Percentile(ordered, 50), Percentile(ordered, 95), Percentile(ordered, 99),
        wallClock.Elapsed.TotalSeconds, errors.Take(100).ToArray());

    var stamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss");
    var jsonPath = Path.Combine(options.OutputDirectory, $"load-{options.Clients}-{stamp}.json");
    var csvPath = Path.Combine(options.OutputDirectory, $"load-{options.Clients}-{stamp}.csv");
    await File.WriteAllTextAsync(jsonPath, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
    await File.WriteAllTextAsync(csvPath,
        "clients,connected,games,move_commits,spectator_joins,samples,errors,p50_ms,p95_ms,p99_ms,elapsed_seconds" + Environment.NewLine +
        $"{report.ClientsRequested},{report.ClientsConnected},{report.GamesCreated},{report.MoveCommits},{report.SpectatorJoins},{report.Samples},{report.Errors},{report.P50Ms:F3},{report.P95Ms:F3},{report.P99Ms:F3},{report.ElapsedSeconds:F3}" + Environment.NewLine);
    Console.WriteLine($"connected={report.ClientsConnected} games={report.GamesCreated} moves={report.MoveCommits} spectators={report.SpectatorJoins} samples={report.Samples} errors={report.Errors}");
    Console.WriteLine($"p50={report.P50Ms:F2}ms p95={report.P95Ms:F2}ms p99={report.P99Ms:F2}ms");
    Console.WriteLine(jsonPath);
    Environment.ExitCode = errors.IsEmpty ? 0 : 2;
}
finally
{
    foreach (var client in clients.OfType<VirtualClient>()) await client.DisposeAsync();
}

static double Percentile(double[] values, double percentile)
{
    if (values.Length == 0) return 0;
    var rank = (percentile / 100d) * (values.Length - 1);
    var low = (int)Math.Floor(rank);
    var high = (int)Math.Ceiling(rank);
    return values[low] + (values[high] - values[low]) * (rank - low);
}

internal sealed class VirtualClient : IAsyncDisposable
{
    private readonly TcpClient _client;
    private readonly NetworkStream _stream;
    private readonly Channel<JsonElement> _messages = Channel.CreateUnbounded<JsonElement>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });
    private readonly CancellationTokenSource _readerCts = new();
    private readonly Task _readerTask;
    private readonly SemaphoreSlim _sendGate = new(1, 1);
    private Task? _heartbeatTask;
    private long _sequence;

    private VirtualClient(int index, TcpClient client)
    {
        Index = index;
        _client = client;
        _stream = client.GetStream();
        _readerTask = Task.Run(() => ReadLoopAsync(_readerCts.Token));
    }

    public int Index { get; }
    public string PlayerId { get; private set; } = string.Empty;
    public string Token { get; private set; } = string.Empty;

    public static async Task<VirtualClient> ConnectAsync(int index, string runTag, LoadOptions options, CancellationToken cancellationToken)
    {
        var tcp = new TcpClient();
        await tcp.ConnectAsync(options.Host, options.Port, cancellationToken);
        var client = new VirtualClient(index, tcp);
        await client.SendAsync("HELLO", new { protocolVersion = "1.0", clientName = "UDM18.LoadTest" }, authenticated: false);
        await client.ReadExpectedAsync("HELLO_ACK");
        await client.SendAsync("LOGIN_REQUEST", new { displayName = $"load-{runTag}-{index:D3}" }, authenticated: false);
        var login = await client.ReadExpectedAsync("LOGIN_RESULT");
        var payload = login.GetProperty("payload");
        client.Token = payload.GetProperty("token").GetString()!;
        client.PlayerId = payload.GetProperty("player").GetProperty("playerId").GetString()!;
        client.StartHeartbeat();
        return client;
    }

    public async Task MeasurePingAsync(
        int durationSeconds,
        int intervalMs,
        ConcurrentBag<double> latencies,
        CancellationToken cancellationToken)
    {
        var elapsed = Stopwatch.StartNew();
        var nonce = 0;
        while (elapsed.Elapsed < TimeSpan.FromSeconds(durationSeconds))
        {
            var sw = Stopwatch.StartNew();
            await SendAsync("PING", new { nonce = $"{Index}-{++nonce}", timestamp = DateTimeOffset.UtcNow }, cancellationToken: cancellationToken);
            await ReadExpectedAsync("PONG", cancellationToken);
            sw.Stop();
            latencies.Add(sw.Elapsed.TotalMilliseconds);
            await Task.Delay(intervalMs, cancellationToken);
        }
    }

    public async Task SendAsync(
        string type,
        object payload,
        string? roomId = null,
        bool authenticated = true,
        CancellationToken cancellationToken = default)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new
        {
            protocolVersion = "1.0", type, requestId = Guid.NewGuid().ToString("N"),
            sessionToken = authenticated ? Token : null, roomId,
            clientSequence = Interlocked.Increment(ref _sequence), sentAtUtc = DateTimeOffset.UtcNow, payload
        });
        await _sendGate.WaitAsync(cancellationToken);
        try { await TcpFrameCodec.WriteFrameAsync(_stream, bytes, cancellationToken); }
        finally { _sendGate.Release(); }
    }

    public async Task<JsonElement> ReadExpectedAsync(string expectedType, CancellationToken cancellationToken = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        while (true)
        {
            var root = await _messages.Reader.ReadAsync(timeout.Token);
            var type = root.GetProperty("type").GetString();
            if (type == "ERROR_RESPONSE") throw new InvalidOperationException(root.GetProperty("payload").ToString());
            if (type == expectedType) return root;
        }
    }

    private async Task ReadLoopAsync(CancellationToken cancellationToken)
    {
        Exception? failure = null;
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var bytes = await TcpFrameCodec.ReadFrameAsync(_stream, cancellationToken);
                if (bytes is null) break;
                using var document = JsonDocument.Parse(bytes);
                await _messages.Writer.WriteAsync(document.RootElement.Clone(), cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception ex) { failure = ex; }
        finally { _messages.Writer.TryComplete(failure); }
    }

    private void StartHeartbeat()
    {
        _heartbeatTask = Task.Run(async () =>
        {
            try
            {
                using var timer = new PeriodicTimer(TimeSpan.FromSeconds(8));
                while (await timer.WaitForNextTickAsync(_readerCts.Token))
                    await SendAsync("PING", new
                    {
                        nonce = $"heartbeat-{Index}-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}",
                        timestamp = DateTimeOffset.UtcNow
                    }, cancellationToken: _readerCts.Token);
            }
            catch (OperationCanceledException) when (_readerCts.IsCancellationRequested) { }
        });
    }

    public async ValueTask DisposeAsync()
    {
        _readerCts.Cancel();
        _client.Dispose();
        if (_heartbeatTask is not null)
            try { await _heartbeatTask; } catch { }
        try { await _readerTask; } catch { }
        await _stream.DisposeAsync();
        _sendGate.Dispose();
        _readerCts.Dispose();
    }
}

internal sealed record LoadReport(
    DateTimeOffset MeasuredAtUtc,
    string Host,
    int Port,
    int ClientsRequested,
    int ClientsConnected,
    int GamesCreated,
    int MoveCommits,
    int SpectatorJoins,
    int DurationSeconds,
    int Samples,
    int Errors,
    double P50Ms,
    double P95Ms,
    double P99Ms,
    double ElapsedSeconds,
    IReadOnlyList<string> ErrorDetails);

internal sealed record LoadOptions(
    string Host,
    int Port,
    int Clients,
    int Games,
    int Spectators,
    int DurationSeconds,
    int PingIntervalMs,
    string OutputDirectory)
{
    public static LoadOptions Parse(string[] args)
    {
        string Read(string name, string fallback)
        {
            var index = Array.IndexOf(args, name);
            return index >= 0 && index + 1 < args.Length ? args[index + 1] : fallback;
        }
        var clients = Math.Clamp(int.Parse(Read("--clients", "10")), 1, 1000);
        return new(
            Read("--host", "127.0.0.1"), int.Parse(Read("--port", "5000")), clients,
            Math.Clamp(int.Parse(Read("--games", (clients / 2).ToString())), 0, clients / 2),
            Math.Clamp(int.Parse(Read("--spectators", Math.Min(5, clients).ToString())), 0, clients),
            Math.Clamp(int.Parse(Read("--duration", "10")), 1, 3600),
            Math.Clamp(int.Parse(Read("--interval", "250")), 10, 60000),
            Path.GetFullPath(Read("--output", Path.Combine("Extra", "test-evidence", "load"))));
    }
}
