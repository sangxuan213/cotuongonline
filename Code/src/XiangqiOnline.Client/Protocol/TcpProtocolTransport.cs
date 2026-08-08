using System.Buffers.Binary;
using System.IO;
using System.Net.Sockets;
using System.Text.Json;

namespace UDM18.Client.Protocol;

public sealed class TcpProtocolTransport : IProtocolTransport
{
    public const int MaxPayloadBytes = 65_536;
    private readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);
    private readonly SemaphoreSlim _sendGate = new(1, 1);
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private TcpClient? _client;
    private NetworkStream? _stream;
    private CancellationTokenSource? _receiveCts;
    private Task? _receiveTask;

    public ConnectionState State { get; private set; } = ConnectionState.Disconnected;
    public event Action<ConnectionState, string?>? StateChanged;
    public Func<JsonElement, Task>? MessageHandler { get; set; }

    public async Task ConnectAsync(string host, int port, CancellationToken cancellationToken)
    {
        await _lifecycleGate.WaitAsync(cancellationToken);
        try
        {
            if (State is ConnectionState.Connecting or ConnectionState.Connected) return;
            await DisconnectCoreAsync();
            SetState(ConnectionState.Connecting);
            _client = new TcpClient { NoDelay = true };
            await _client.ConnectAsync(host, port, cancellationToken);
            _stream = _client.GetStream();
            _receiveCts = new CancellationTokenSource();
            _receiveTask = ReceiveLoopAsync(_receiveCts.Token);
            SetState(ConnectionState.Connected);
        }
        catch (Exception ex)
        {
            await DisconnectCoreAsync();
            SetState(ConnectionState.Failed, ex is OperationCanceledException ? "Đã hủy kết nối." : ex.Message);
            throw;
        }
        finally { _lifecycleGate.Release(); }
    }

    public async Task SendAsync(object envelope, CancellationToken cancellationToken = default)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(envelope, _json);
        if (payload.Length > MaxPayloadBytes) throw new InvalidDataException("Payload vượt giới hạn 64 KiB.");
        var header = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(header, payload.Length);
        await _sendGate.WaitAsync(cancellationToken);
        try
        {
            var stream = _stream ?? throw new InvalidOperationException("Client chưa kết nối Server.");
            await stream.WriteAsync(header, cancellationToken);
            await stream.WriteAsync(payload, cancellationToken);
            await stream.FlushAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException)
        {
            _stream?.Close();
            _client?.Close();
            SetState(ConnectionState.Failed, ex.Message);
            throw;
        }
        finally { _sendGate.Release(); }
    }

    public async Task DisconnectAsync()
    {
        await _lifecycleGate.WaitAsync();
        try
        {
            await DisconnectCoreAsync();
            SetState(ConnectionState.Disconnected);
        }
        finally { _lifecycleGate.Release(); }
    }

    public void Abort()
    {
        _receiveCts?.Cancel();
        _stream?.Close();
        _client?.Close();
    }

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            var header = new byte[4];
            while (!cancellationToken.IsCancellationRequested)
            {
                await ReadExactlyAsync(header, cancellationToken);
                var length = BinaryPrimitives.ReadInt32BigEndian(header);
                if (length is <= 0 or > MaxPayloadBytes)
                    throw new InvalidDataException($"INVALID_FRAME_LENGTH: {length}");
                var payload = new byte[length];
                await ReadExactlyAsync(payload, cancellationToken);
                using var document = JsonDocument.Parse(payload);
                var handler = MessageHandler;
                if (handler is not null) await handler(document.RootElement.Clone());
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception ex)
        {
            _stream?.Close();
            _client?.Close();
            SetState(ConnectionState.Failed, ex.Message);
        }
    }

    private async Task ReadExactlyAsync(Memory<byte> target, CancellationToken cancellationToken)
    {
        var stream = _stream ?? throw new IOException("Socket đã đóng.");
        var read = 0;
        while (read < target.Length)
        {
            var count = await stream.ReadAsync(target[read..], cancellationToken);
            if (count == 0) throw new IOException("Server đã đóng kết nối.");
            read += count;
        }
    }

    private async Task DisconnectCoreAsync()
    {
        _receiveCts?.Cancel();
        _stream?.Close();
        _client?.Close();
        if (_receiveTask is not null)
        {
            try { await _receiveTask; } catch { }
        }
        _receiveCts?.Dispose();
        _receiveCts = null;
        _stream = null;
        _client = null;
        _receiveTask = null;
    }

    private void SetState(ConnectionState state, string? error = null)
    {
        State = state;
        StateChanged?.Invoke(state, error);
    }

    public async ValueTask DisposeAsync()
    {
        await _lifecycleGate.WaitAsync();
        try { await DisconnectCoreAsync(); }
        finally
        {
            _lifecycleGate.Release();
            _lifecycleGate.Dispose();
            _sendGate.Dispose();
        }
    }
}
