using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace XiangqiOnline.Shared.Transport
{
    /// <summary>
    /// Wraps TcpListener with validated bind address/port, controlled start/stop,
    /// and events instead of exceptions escaping into caller's hot path.
    /// Day 2 scope: accept connections only. Frame reading (TcpFrameCodec) and
    /// per-connection session state are wired in on Day 3+.
    /// </summary>
    public sealed class TcpServerHost : IAsyncDisposable
    {
        private readonly IPAddress _bindAddress;
        private readonly int _port;
        private TcpListener? _listener;
        private CancellationTokenSource? _cts;
        private Task? _acceptLoopTask;

        /// <summary>Raised on the accept loop for every client that connects.</summary>
        public event Action<TcpClient>? ClientAccepted;

        /// <summary>Raised if the accept loop itself dies unexpectedly (not a normal stop).</summary>
        public event Action<Exception>? AcceptLoopFaulted;

        public bool IsListening { get; private set; }

        /// <summary>Actual bound port — useful in tests where Port is 0 (OS-assigned).</summary>
        public int? BoundPort => _listener?.LocalEndpoint is IPEndPoint ep ? ep.Port : null;

        public TcpServerHost(string bindAddress, int port)
        {
            if (!IPAddress.TryParse(bindAddress, out var parsed))
                throw new ArgumentException($"Địa chỉ IP không hợp lệ: '{bindAddress}'.", nameof(bindAddress));
            if (port < 0 || port > 65535)
                throw new ArgumentOutOfRangeException(nameof(port), $"Port không hợp lệ: {port}. Phải trong khoảng 0-65535.");

            _bindAddress = parsed;
            _port = port;
        }

        /// <summary>
        /// Starts listening. Throws InvalidOperationException (not a raw SocketException)
        /// on bind failure — e.g. port already in use, or no permission — so callers get
        /// a clear message instead of a crash.
        /// </summary>
        public Task StartAsync(CancellationToken ct = default)
        {
            if (IsListening)
                throw new InvalidOperationException("Server đã đang lắng nghe.");

            try
            {
                _listener = new TcpListener(_bindAddress, _port);
                // A larger pending-connection queue prevents short connection bursts
                // (for example a classroom opening the client together) from being dropped.
                _listener.Start(512);
            }
            catch (SocketException ex)
            {
                throw new InvalidOperationException(
                    $"Không thể bind {_bindAddress}:{_port}. Có thể port đang bị chiếm hoặc không có quyền. Chi tiết: {ex.Message}", ex);
            }

            IsListening = true;
            _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            _acceptLoopTask = AcceptLoopAsync(_cts.Token);
            return Task.CompletedTask;
        }

        private async Task AcceptLoopAsync(CancellationToken ct)
        {
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    TcpClient client;
                    try
                    {
                        client = await _listener!.AcceptTcpClientAsync(ct).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        break; // stopping normally
                    }
                    catch (ObjectDisposedException)
                    {
                        break; // listener was stopped/disposed
                    }

                    ClientAccepted?.Invoke(client);
                }
            }
            catch (Exception ex)
            {
                AcceptLoopFaulted?.Invoke(ex);
            }
        }

        public async Task StopAsync()
        {
            if (!IsListening) return;

            IsListening = false;
            _cts?.Cancel();
            _listener?.Stop();

            if (_acceptLoopTask != null)
            {
                try { await _acceptLoopTask.ConfigureAwait(false); }
                catch { /* already surfaced via AcceptLoopFaulted if relevant */ }
            }

            _cts?.Dispose();
            _cts = null;
            _listener = null;
        }

        public async ValueTask DisposeAsync() => await StopAsync().ConfigureAwait(false);
    }
}
