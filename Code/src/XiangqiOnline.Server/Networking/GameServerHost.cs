using System;
using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using XiangqiOnline.Shared.Transport;

namespace XiangqiOnline.Server.Networking
{
    /// <summary>
    /// Wiring layer that connects the existing TcpServerHost accept loop to the
    /// production per-connection pipeline (ClientConnectionHandler -> MessageRouter).
    /// Program.cs and the real TCP integration tests share this exact class so the
    /// tested path is the shipped path. Builds on the TV1 networking stack; it does
    /// not open its own sockets or reinvent framing.
    /// </summary>
    public sealed class GameServerHost : IAsyncDisposable
    {
        private readonly TcpServerHost _tcpHost;
        private readonly MessageRouter _router;
        private readonly ConcurrentDictionary<long, ClientConnectionHandler> _connections = new();
        private long _nextConnectionId;
        private CancellationTokenSource? _cts;
        private bool _started;

        /// <summary>Raised for every accepted connection (informational).</summary>
        public event Action<string>? ConnectionOpened;

        /// <summary>Raised when a connection closes for whatever reason.</summary>
        public event Action<long>? ConnectionClosed;

        /// <summary>Actual bound port — useful in tests where Port is 0 (OS-assigned).</summary>
        public int? BoundPort => _tcpHost.BoundPort;

        public int ActiveConnectionCount => _connections.Count;

        public GameServerHost(string bindAddress, int port, MessageRouter router)
        {
            _router = router ?? throw new ArgumentNullException(nameof(router));
            _tcpHost = new TcpServerHost(bindAddress, port);
        }

        public async Task StartAsync(CancellationToken ct = default)
        {
            if (_started) throw new InvalidOperationException("Server đã đang chạy.");
            _started = true;
            _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            _tcpHost.ClientAccepted += OnClientAccepted;
            await _tcpHost.StartAsync(_cts.Token).ConfigureAwait(false);
        }

        private void OnClientAccepted(TcpClient client)
        {
            var connection = new ClientConnectionHandler(client, _router);
            long id = Interlocked.Increment(ref _nextConnectionId);
            _connections[id] = connection;
            ConnectionOpened?.Invoke(id.ToString());

            _ = Task.Run(async () =>
            {
                try
                {
                    await connection.RunAsync(_cts?.Token ?? default).ConfigureAwait(false);
                }
                finally
                {
                    _connections.TryRemove(id, out _);
                    await connection.DisposeAsync().ConfigureAwait(false);
                    ConnectionClosed?.Invoke(id);
                }
            });
        }

        public Task StopAsync() => _tcpHost.StopAsync();

        public async ValueTask DisposeAsync()
        {
            _cts?.Cancel();
            await StopAsync().ConfigureAwait(false);
            foreach (var connection in _connections.Values)
                await connection.DisposeAsync().ConfigureAwait(false);
            _connections.Clear();
            _cts?.Dispose();
            _cts = null;
        }
    }
}