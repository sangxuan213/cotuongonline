using System.Text.Json;

namespace UDM18.Client.Protocol;

public enum ConnectionState { Disconnected, Connecting, Connected, Failed }

public interface IProtocolTransport : IAsyncDisposable
{
    ConnectionState State { get; }
    event Action<ConnectionState, string?>? StateChanged;
    Func<JsonElement, Task>? MessageHandler { get; set; }
    Task ConnectAsync(string host, int port, CancellationToken cancellationToken);
    Task SendAsync(object envelope, CancellationToken cancellationToken = default);
    Task DisconnectAsync();
    void Abort();
}
