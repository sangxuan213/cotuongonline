using System;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using XiangqiOnline.Shared.Protocol;
using XiangqiOnline.Shared.Transport;

namespace XiangqiOnline.Server.Networking
{
    /// <summary>
    /// Per-connection lifecycle on the Server side: wires the accepted socket into
    /// the locked receive stack (ConnectionReceiveLoop + TcpFrameCodec), parses each
    /// validated frame into a RequestEnvelope, routes it through MessageRouter, and
    /// writes responses back over the same socket. Also owns the connection timeout
    /// and disposal, and exposes connection-level events for Program.cs / tests.
    /// </summary>
    public sealed class ClientConnectionHandler : IAsyncDisposable
    {
        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

        private readonly TcpClient _client;
        private readonly NetworkStream _stream;
        private readonly ConnectionReceiveLoop _receiveLoop;
        private readonly MessageRouter _router;
        private readonly CancellationTokenSource _cts = new();
        private readonly object _gate = new();
        private Task? _runTask;
        private bool _disposed;

        /// <summary>Raised when the peer disconnects cleanly or the connection faults.</summary>
        public event Action<ClientConnectionHandler>? ConnectionClosed;

        public ClientConnectionHandler(TcpClient client, MessageRouter router)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _router = router ?? throw new ArgumentNullException(nameof(router));
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
            await TcpFrameCodec.WriteFrameAsync(_stream, payload, ct).ConfigureAwait(false);
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
            try
            {
                _receiveLoop.FrameReceived += OnFrameReceived;
                _receiveLoop.ProtocolViolation += OnProtocolViolation;
                _receiveLoop.Disconnected += OnDisconnected;
                await _receiveLoop.RunAsync(_cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Server shutting down or connection disposed — normal.
            }
            finally
            {
                _receiveLoop.FrameReceived -= OnFrameReceived;
                _receiveLoop.ProtocolViolation -= OnProtocolViolation;
                _receiveLoop.Disconnected -= OnDisconnected;
            }
        }

        private void OnFrameReceived(byte[] payload, string json)
        {
            _ = DispatchFrameAsync(json);
        }

        private async Task DispatchFrameAsync(string json)
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

                await _router.DispatchAsync(envelope, this).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                try
                {
                    await SendErrorAsync("INTERNAL_SERVER_ERROR", ex.Message, null).ConfigureAwait(false);
                }
                catch
                {
                    // No point trying to report an error on a broken socket.
                }
            }
        }

        private void OnProtocolViolation(string errorCode, string message)
        {
            _ = SendErrorAsync(errorCode, message, null);
            _ = CloseAsync();
        }

        private void OnDisconnected() => _ = CloseAsync();

        private async Task CloseAsync()
        {
            try
            {
                _cts.Cancel();
                _client.Client.Shutdown(SocketShutdown.Both);
            }
            catch
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
            catch
            {
                // Best effort.
            }

            _stream.Dispose();
            _client.Dispose();
            _cts.Dispose();
        }
    }
}