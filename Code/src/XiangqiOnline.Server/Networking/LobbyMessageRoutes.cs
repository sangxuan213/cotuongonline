using System.Threading;
using System.Threading.Tasks;
using XiangqiOnline.Server.Lobby;

namespace XiangqiOnline.Server.Networking
{
    /// <summary>
    /// Single place that registers the lobby wire routes (LOGIN_REQUEST and
    /// PLAYER_LIST_REQUEST) so the shipped Program.cs and the real-TCP integration
    /// tests exercise the exact same wiring.
    /// </summary>
    public static class LobbyMessageRoutes
    {
        public static void Register(
            MessageRouter router,
            PlayerSessionDirectory directory,
            ChallengeManager challenges,
            IConnectionRegistry connections)
        {
            router.Register("LOGIN_REQUEST", (request, connection, ct) =>
                LoginMessageHandler.HandleAsync(request, connection, directory, ct));

            router.Register("PLAYER_LIST_REQUEST", (request, connection, ct) =>
                PlayerListMessageHandler.HandleAsync(request, connection, directory, ct));

            router.Register("CHALLENGE_SEND", (request, connection, ct) =>
                ChallengeMessageHandler.HandleAsync(request, connection, directory, challenges, connections, ct));

            router.Register("CHALLENGE_ACCEPT", (request, connection, ct) =>
                AcceptChallengeMessageHandler.HandleAsync(request, connection, directory, challenges, connections, ct));

            router.Register("CHALLENGE_REJECT", (request, connection, ct) =>
                RejectChallengeMessageHandler.HandleAsync(request, connection, directory, challenges, connections, ct));
        }
    }
}
