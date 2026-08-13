using System;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace XiangqiOnline.Shared.Transport
{
    /// <summary>
    /// Connection states surfaced to the UI layer (Day 4 wires this into
    /// ConnectionViewModel). Matches Technical Contract §14.1 Connection screen.
    /// </summary>
    public enum ConnectionState
    {
        Disconnected,
        Connecting,
        Connected,
        Failed
    }

    /// <summary>
    /// Client-side TCP connection with controlled connect/cancel/disconnect and
    /// state mapping. Day 2 scope: raw transport only. Frame read/write
    /// (TcpFrameCodec) is layered on top starting Day 3.
    /// </summary>
    public sealed class TcpClientService : IAsyncDisposable
    {
        private TcpClient? _client;
        private CancellationTokenSource? _connectCts;

        public ConnectionState State { get; private set; } = ConnectionState.Disconnected;
        public event Action<ConnectionState>? StateChanged;

        public NetworkStream? Stream { get; private set; }

        /// <summary>
        /// Connects to host:port. Throws InvalidOperationException on failure with a
        /// human-readable message (bad host, refused connection, timeout, etc.) instead
        /// of letting a raw SocketException escape. Sets state to Failed on error,
        /// Disconnected (not Failed) if the caller cancels via the token.
        /// </summary>
        public async Task ConnectAsync(string host, int port, CancellationToken ct = default)
        {
            if (State is ConnectionState.Connecting or ConnectionState.Connected)
                throw new InvalidOperationException($"Không thể connect khi đang ở trạng thái {State}.");

            SetState(ConnectionState.Connecting);
            _connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

            try
            {
                _client = new TcpClient();
                await _client.ConnectAsync(host, port, _connectCts.Token).ConfigureAwait(false);
                Stream = _client.GetStream();
                SetState(ConnectionState.Connected);
            }
            catch (OperationCanceledException)
            {
                CleanupSocket();
                SetState(ConnectionState.Disconnected); // caller cancelled — not a failure
                throw;
            }
            catch (Exception ex) when (ex is SocketException or ObjectDisposedException)
            {
                CleanupSocket();
                SetState(ConnectionState.Failed);
                throw new InvalidOperationException($"Không thể kết nối tới {host}:{port}. Chi tiết: {ex.Message}", ex);
            }
        }

        /// <summary>Cancels an in-flight ConnectAsync. No-op if not currently connecting.</summary>
        public void CancelConnect() => _connectCts?.Cancel();

        public Task DisconnectAsync()
        {
            CleanupSocket();
            SetState(ConnectionState.Disconnected);
            return Task.CompletedTask;
        }

        private void CleanupSocket()
        {
            Stream?.Dispose();
            Stream = null;
            _client?.Close();
            _client?.Dispose();
            _client = null;
            _connectCts?.Dispose();
            _connectCts = null;
        }

        private void SetState(ConnectionState state)
        {
            State = state;
            StateChanged?.Invoke(state);
        }

        public async ValueTask DisposeAsync() => await DisconnectAsync().ConfigureAwait(false);
    }
}
