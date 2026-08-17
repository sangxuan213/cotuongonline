using System;
using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using XiangqiOnline.Server.Lobby;
using XiangqiOnline.Shared.Protocol;
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
    public sealed class GameServerHost : IAsyncDisposable, IConnectionRegistry
    {
        private readonly TcpServerHost _tcpHost;
        private readonly MessageRouter _router;
        private readonly PlayerSessionDirectory? _players;
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

        public bool TryGetConnection(string connectionId, out ClientConnectionHandler connection)
        {
            connection = null!;
            return long.TryParse(connectionId, out var id) && _connections.TryGetValue(id, out connection!);
        }

        public GameServerHost(string bindAddress, int port, MessageRouter router)
            : this(bindAddress, port, router, null)
        {
        }

        public GameServerHost(
            string bindAddress,
            int port,
            MessageRouter router,
            PlayerSessionDirectory? players)
        {
            _router = router ?? throw new ArgumentNullException(nameof(router));
            _players = players;
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
            long id = Interlocked.Increment(ref _nextConnectionId);
            var connection = new ClientConnectionHandler(client, _router, id.ToString());
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
                    await MarkPlayerOfflineAndBroadcastAsync(connection.ConnectionId).ConfigureAwait(false);
                    ConnectionClosed?.Invoke(id);
                }
            });
        }

        private async Task MarkPlayerOfflineAndBroadcastAsync(string connectionId)
        {
            if (_players is null || !_players.TryGetByConnectionId(connectionId, out _))
                return;

            _players.MarkOfflineByConnectionId(connectionId, DateTimeOffset.UtcNow);

            var envelope = new ServerEventEnvelope<object>
            {
                Type = "PLAYER_LIST_UPDATED",
                EventId = Guid.NewGuid().ToString("N"),
                ServerTimeUtc = DateTimeOffset.UtcNow,
                Payload = new
                {
                    players = _players.GetSnapshot().Select(player => new
                    {
                        playerId = player.PlayerId,
                        displayName = player.DisplayName,
                        status = player.Status.ToString()
                    }).ToArray()
                }
            };

            foreach (var remainingConnection in _connections.Values)
            {
                try
                {
                    await remainingConnection.SendAsync(envelope).ConfigureAwait(false);
                }
                catch
                {
                    // A concurrently closing peer will be handled by its own lifecycle.
                }
            }
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
