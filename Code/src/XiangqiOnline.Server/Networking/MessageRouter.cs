using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using XiangqiOnline.Shared.Protocol;

namespace XiangqiOnline.Server.Networking
{
    /// <summary>
    /// Routes one decoded RequestEnvelope to the handler registered for its Type.
    /// Unknown types get a uniform ERROR_RESPONSE so a real client never hangs
    /// waiting on a request that the production build cannot serve yet.
    /// </summary>
    public sealed class MessageRouter
    {
        public delegate Task RouteHandler(
            RequestEnvelope<JsonElement> request,
            ClientConnectionHandler connection,
            CancellationToken ct);

        private readonly Dictionary<string, RouteHandler> _routes = new(StringComparer.Ordinal);

        public void Register(string type, RouteHandler handler)
        {
            if (string.IsNullOrWhiteSpace(type))
                throw new ArgumentException("Message type must not be blank.", nameof(type));
            _routes[type] = handler ?? throw new ArgumentNullException(nameof(handler));
        }

        public bool IsRegistered(string type) => _routes.ContainsKey(type);

        public async Task DispatchAsync(
            RequestEnvelope<JsonElement> request,
            ClientConnectionHandler connection,
            CancellationToken ct = default)
        {
            if (_routes.TryGetValue(request.Type, out var handler))
            {
                await handler(request, connection, ct).ConfigureAwait(false);
                return;
            }

            await connection.SendErrorAsync(
                "INVALID_MESSAGE_TYPE",
                $"Message type '{request.Type}' is not supported.",
                request.RequestId,
                ct).ConfigureAwait(false);
        }
    }
}