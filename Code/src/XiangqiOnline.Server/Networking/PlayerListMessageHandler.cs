using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using XiangqiOnline.Server.Lobby;
using XiangqiOnline.Shared.Protocol;

namespace XiangqiOnline.Server.Networking
{
    /// <summary>
    /// PLAYER_LIST_REQUEST route: replies PLAYER_LIST_UPDATED carrying the current
    /// PlayerSessionDirectory snapshot, so the logged-in client's lobby list renders.
    /// </summary>
    public static class PlayerListMessageHandler
    {
        public static async Task HandleAsync(
            RequestEnvelope<JsonElement> request,
            ClientConnectionHandler connection,
            PlayerSessionDirectory directory,
            CancellationToken ct)
        {
            var snapshot = directory.GetSnapshot();
            var envelope = new ServerEventEnvelope<object>
            {
                Type = "PLAYER_LIST_UPDATED",
                EventId = Guid.NewGuid().ToString("N"),
                CausationRequestId = request.RequestId,
                ServerTimeUtc = DateTimeOffset.UtcNow,
                Payload = new
                {
                    players = snapshot.Select(player => new
                    {
                        playerId = player.PlayerId,
                        displayName = player.DisplayName,
                        status = player.Status.ToString()
                    }).ToArray()
                }
            };

            await connection.SendAsync(envelope, ct).ConfigureAwait(false);
        }
    }
}
