using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using XiangqiOnline.Shared.Transport;
using Xunit;

namespace XiangqiOnline.Shared.Tests.Transport
{
    public class ConnectionTests
    {
        /// <summary>Grabs a free loopback port and immediately releases it, so nothing listens there.</summary>
        private static int GetClosedPort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }

        [Fact]
        public async Task Server_AcceptsClient_OnLocalhost()
        {
            // Port 0 = OS picks a free port, so tests never collide with each other.
            await using var server = new TcpServerHost("127.0.0.1", 0);
            var accepted = new TaskCompletionSource<bool>();
            server.ClientAccepted += _ => accepted.TrySetResult(true);

            await server.StartAsync();
            Assert.True(server.IsListening);
            Assert.NotNull(server.BoundPort);

            await using var client = new TcpClientService();
            await client.ConnectAsync("127.0.0.1", server.BoundPort!.Value);

            var acceptedInTime = await Task.WhenAny(accepted.Task, Task.Delay(2000)) == accepted.Task;
            Assert.True(acceptedInTime, "Server phải nhận được kết nối trong 2 giây.");
            Assert.Equal(ConnectionState.Connected, client.State);
        }

        [Fact]
        public async Task Client_ConnectToClosedPort_SetsFailedState_DoesNotThrowUnhandled()
        {
            // Nothing listening on this port -> connection must be refused, not crash.
            await using var client = new TcpClientService();
            var closedPort = GetClosedPort();

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => client.ConnectAsync("127.0.0.1", closedPort));

            Assert.Equal(ConnectionState.Failed, client.State);
        }

        [Fact]
        public async Task Client_CancelConnect_SetsDisconnectedNotFailed()
        {
            // Connect to a non-routable address so the attempt hangs long enough to cancel.
            await using var client = new TcpClientService();
            using var cts = new CancellationTokenSource();

            var connectTask = client.ConnectAsync("10.255.255.1", 65000, cts.Token);
            cts.CancelAfter(100);

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => connectTask);
            Assert.Equal(ConnectionState.Disconnected, client.State);
        }

        [Fact]
        public void Server_InvalidBindAddress_ThrowsImmediately_NotOnStart()
        {
            Assert.Throws<ArgumentException>(() => new TcpServerHost("not-an-ip", 5000));
        }

        [Fact]
        public void Server_PortOutOfRange_ThrowsImmediately()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new TcpServerHost("127.0.0.1", 70000));
        }

        [Fact]
        public async Task Client_DisconnectAsync_ResetsStateToDisconnected()
        {
            await using var server = new TcpServerHost("127.0.0.1", 0);
            await server.StartAsync();

            var client = new TcpClientService();
            await client.ConnectAsync("127.0.0.1", server.BoundPort!.Value);
            Assert.Equal(ConnectionState.Connected, client.State);

            await client.DisconnectAsync();
            Assert.Equal(ConnectionState.Disconnected, client.State);
        }
    }
}
