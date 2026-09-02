using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using XiangqiOnline.Server;
using XiangqiOnline.Shared.Models;
using XiangqiOnline.Shared.Protocol;
using XiangqiOnline.Shared.Transport;

namespace XiangqiOnline.Server.Networking;

/// <summary>
/// Per-connection lifecycle on the Server side: wires the accepted socket into
/// the locked receive stack (ConnectionReceiveLoop + TcpFrameCodec), parses each
/// validated frame into a RequestEnvelope, routes it through MessageRouter, and
/// writes responses back over the same socket. Also owns the connection timeout
/// and disposal, and exposes connection-level events for Program.cs / tests.
/// </summary>
public sealed class ClientConnectionHandler : IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseUpper));
        return options;
    }

    private readonly TcpClient _client;
    private readonly NetworkStream _stream;
    private readonly ConnectionReceiveLoop _receiveLoop;
    private readonly MessageRouter _router;
    private readonly CancellationTokenSource _cts = new();
    private readonly object _gate = new();
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly Queue<DateTimeOffset> _requestTimes = new();
    private readonly HashSet<string> _requestIds = new(StringComparer.Ordinal);
    private readonly Queue<string> _requestIdOrder = new();
    private readonly object _requestGate = new();
    private readonly Channel<string> _frames = Channel.CreateBounded<string>(new BoundedChannelOptions(256)
    {
        SingleReader = true,
        SingleWriter = true,
        FullMode = BoundedChannelFullMode.DropWrite
    });
    private readonly int _requestsPerSecond;
    private readonly TimeSpan _heartbeatTimeout;
    private Task? _runTask;
    private bool _disposed;
    private int _closed;

    /// <summary>Server-assigned id that identifies this connection in session state.</summary>
    public string ConnectionId { get; }

    /// <summary>Remote IP used by process-wide authentication throttling.</summary>
    public string RemoteAddress
    {
        get
        {
            try { return (_client.Client.RemoteEndPoint as IPEndPoint)?.Address.ToString() ?? "unknown"; }
            catch (ObjectDisposedException) { return "unknown"; }
            catch (SocketException) { return "unknown"; }
        }
    }

    /// <summary>Raised when the peer disconnects cleanly or the connection faults.</summary>
    public event Action<ClientConnectionHandler>? ConnectionClosed;
    public DateTimeOffset LastActivityUtc { get; private set; } = DateTimeOffset.UtcNow;

    public ClientConnectionHandler(
        TcpClient client,
        MessageRouter router,
        string connectionId,
        int requestsPerSecond = 40,
        TimeSpan? heartbeatTimeout = null)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _router = router ?? throw new ArgumentNullException(nameof(router));
        if (string.IsNullOrWhiteSpace(connectionId))
            throw new ArgumentException("Connection id is required.", nameof(connectionId));
        ConnectionId = connectionId;
        _requestsPerSecond = requestsPerSecond > 0 ? requestsPerSecond : throw new ArgumentOutOfRangeException(nameof(requestsPerSecond));
        _heartbeatTimeout = heartbeatTimeout ?? TimeSpan.FromSeconds(30);
        if (_heartbeatTimeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(heartbeatTimeout));
        _stream = client.GetStream();
        _receiveLoop = new ConnectionReceiveLoop(_stream);
    }

    /// <summary>Starts the receive loop; never throws for protocol-level faults.</summary>
    public Task RunAsync(CancellationToken ct = default)
    {
        lock (_gate)
        {
            if (_runTask is not null)
                return _runTask;
            _runTask = RunInternalAsync(ct);
            return _runTask;
        }
    }

    /// <summary>Sends a ServerEventEnvelope framed by the locked TcpFrameCodec.</summary>
    public async Task SendAsync(ServerEventEnvelope<object> envelope, CancellationToken ct = default)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(envelope, JsonOptions);
        await _writeGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await TcpFrameCodec.WriteFrameAsync(_stream, payload, ct).ConfigureAwait(false);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    public async Task SendErrorAsync(string errorCode, string message, string? causationRequestId, CancellationToken ct = default)
    {
        var envelope = new ServerEventEnvelope<object>
        {
            Type = "ERROR_RESPONSE",
            EventId = Guid.NewGuid().ToString("N"),
            CausationRequestId = causationRequestId,
            ServerTimeUtc = DateTimeOffset.UtcNow,
            Payload = new { errorCode, message }
        };
        await SendAsync(envelope, ct).ConfigureAwait(false);
    }

    private async Task RunInternalAsync(CancellationToken ct)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _cts.Token);
        var processTask = ProcessFramesAsync(linked.Token);
        var heartbeatTask = MonitorHeartbeatAsync(linked.Token);
        try
        {
            _receiveLoop.FrameReceived += OnFrameReceived;
            _receiveLoop.ProtocolViolation += OnProtocolViolation;
            _receiveLoop.Disconnected += OnDisconnected;
            await _receiveLoop.RunAsync(linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Server shutting down or connection disposed — normal.
        }
        finally
        {
            linked.Cancel();
            _frames.Writer.TryComplete();
            _receiveLoop.FrameReceived -= OnFrameReceived;
            _receiveLoop.ProtocolViolation -= OnProtocolViolation;
            _receiveLoop.Disconnected -= OnDisconnected;
            try { await Task.WhenAll(processTask, heartbeatTask).ConfigureAwait(false); }
            catch (OperationCanceledException) { }
        }
    }

    private void OnFrameReceived(byte[] payload, string json)
    {
        if (!_frames.Writer.TryWrite(json))
        {
            _ = SendErrorAsync(ErrorCodes.RATE_LIMITED, "Connection input queue is full.", null);
            _ = CloseAsync();
        }
    }

    private async Task ProcessFramesAsync(CancellationToken ct)
    {
        await foreach (var json in _frames.Reader.ReadAllAsync(ct).ConfigureAwait(false))
            await DispatchFrameAsync(json, ct).ConfigureAwait(false);
    }

    private async Task DispatchFrameAsync(string json, CancellationToken ct)
    {
        try
        {
            var envelope = JsonSerializer.Deserialize<RequestEnvelope<JsonElement>>(json, JsonOptions);
            if (envelope is null || string.IsNullOrWhiteSpace(envelope.Type))
            {
                await SendErrorAsync(
                    "INVALID_MESSAGE_SCHEMA",
                    "Envelope missing required 'type'.",
                    null).ConfigureAwait(false);
                return;
            }

            LastActivityUtc = DateTimeOffset.UtcNow;
            if (!string.Equals(envelope.ProtocolVersion, ProtocolConstants.ProtocolVersion, StringComparison.Ordinal))
            {
                await SendErrorAsync(ErrorCodes.PROTOCOL_VERSION_UNSUPPORTED,
                    $"Protocol {envelope.ProtocolVersion} is not supported.", envelope.RequestId).ConfigureAwait(false);
                return;
            }
            if (string.IsNullOrWhiteSpace(envelope.RequestId))
            {
                await SendErrorAsync(ErrorCodes.INVALID_MESSAGE_SCHEMA,
                    "Envelope missing required requestId.", null).ConfigureAwait(false);
                return;
            }
            if (!TryAcceptRequest(envelope.RequestId, LastActivityUtc, out var errorCode))
            {
                await SendErrorAsync(errorCode,
                    errorCode == ErrorCodes.RATE_LIMITED ? "Connection request rate exceeded." : "Request was already processed.",
                    envelope.RequestId).ConfigureAwait(false);
                return;
            }

            if (!string.Equals(envelope.Type, "PING", StringComparison.Ordinal))
            {
                var room = string.IsNullOrWhiteSpace(envelope.RoomId) ? "-" : envelope.RoomId[..Math.Min(8, envelope.RoomId.Length)];
                ServerConsoleLog.Info("YÊU CẦU", $"#{ConnectionId} • {envelope.Type} • phòng {room}");
            }

            await _router.DispatchAsync(envelope, this, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            try
            {
                ServerConsoleLog.Error("XỬ LÝ", $"#{ConnectionId} • {ex.GetType().Name}: {ex.Message}");
                await SendErrorAsync(ErrorCodes.INTERNAL_SERVER_ERROR, "The server could not process this request.", null, ct).ConfigureAwait(false);
            }
            catch (Exception sendException)
            {
                ServerConsoleLog.Warning("KẾT NỐI", $"Không thể gửi lỗi tới #{ConnectionId}: {sendException.Message}");
            }
        }
    }

    private bool TryAcceptRequest(string requestId, DateTimeOffset now, out string errorCode)
    {
        lock (_requestGate)
        {
            while (_requestTimes.Count > 0 && now - _requestTimes.Peek() > TimeSpan.FromSeconds(1))
                _requestTimes.Dequeue();
            if (_requestTimes.Count >= _requestsPerSecond)
            {
                errorCode = ErrorCodes.RATE_LIMITED;
                return false;
            }
            if (!_requestIds.Add(requestId))
            {
                errorCode = ErrorCodes.DUPLICATE_REQUEST;
                return false;
            }
            _requestTimes.Enqueue(now);
            _requestIdOrder.Enqueue(requestId);
            while (_requestIdOrder.Count > 2048)
                _requestIds.Remove(_requestIdOrder.Dequeue());
            errorCode = ErrorCodes.OK;
            return true;
        }
    }

    private void OnProtocolViolation(string errorCode, string message)
    {
        _ = SendErrorAsync(errorCode, message, null);
        _ = CloseAsync();
    }

    private void OnDisconnected() => _ = CloseAsync();

    private async Task MonitorHeartbeatAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
        {
            if (DateTimeOffset.UtcNow - LastActivityUtc <= _heartbeatTimeout) continue;
            try { await SendErrorAsync("HEARTBEAT_TIMEOUT", "Connection heartbeat timed out.", null, ct).ConfigureAwait(false); }
            catch (Exception exception)
            {
                ServerConsoleLog.Warning("HEARTBEAT", $"Không thể báo timeout tới #{ConnectionId}: {exception.Message}");
            }
            await CloseAsync().ConfigureAwait(false);
            return;
        }
    }

    private async Task CloseAsync()
    {
        if (Interlocked.Exchange(ref _closed, 1) != 0) return;
        try
        {
            _cts.Cancel();
            _client.Client.Shutdown(SocketShutdown.Both);
        }
        catch (Exception exception) when (exception is SocketException or ObjectDisposedException)
        {
            // Socket may already be gone.
        }
        finally
        {
            _client.Close();
            ConnectionClosed?.Invoke(this);
        }
    }

    public async ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
        }

        _cts.Cancel();
        try
        {
            if (_runTask is not null)
                await _runTask.ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            ServerConsoleLog.Warning("KẾT NỐI", $"Lỗi khi giải phóng #{ConnectionId}: {exception.Message}");
        }

        _stream.Dispose();
        _client.Dispose();
        _writeGate.Dispose();
        _cts.Dispose();
    }
}
