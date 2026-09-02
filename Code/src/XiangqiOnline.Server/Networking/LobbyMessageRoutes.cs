using System.Threading;
using System.Threading.Tasks;
using XiangqiOnline.Server.Lobby;

namespace XiangqiOnline.Server.Networking;

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

        router.Register("WAITING_ROOM_CREATE", (request, connection, ct) =>
            WaitingRoomMessageHandler.CreateAsync(request, connection, directory, challenges, connections, ct));
        router.Register("WAITING_ROOM_LIST", (request, connection, ct) =>
            WaitingRoomMessageHandler.ListAsync(request, connection, directory, challenges, ct));
        router.Register("WAITING_ROOM_JOIN", (request, connection, ct) =>
            WaitingRoomMessageHandler.JoinAsync(request, connection, directory, challenges, connections, ct));
        router.Register("WAITING_ROOM_CANCEL", (request, connection, ct) =>
            WaitingRoomMessageHandler.CancelAsync(request, connection, directory, challenges, connections, ct));

        router.Register("QUICK_CHAT_SEND", (request, connection, ct) =>
            QuickChatMessageHandler.HandleAsync(request, connection, directory, challenges, connections, ct));
    }

    public static void RegisterAccounts(MessageRouter router, AccountMessageHandler accounts)
    {
        router.Register("ACCOUNT_REGISTER_REQUEST", accounts.RegisterAsync);
        router.Register("ACCOUNT_LOGIN_REQUEST", accounts.LoginAsync);
        router.Register("PASSWORD_RESET_REQUEST", accounts.RequestResetAsync);
        router.Register("PASSWORD_RESET_CONFIRM", accounts.ConfirmResetAsync);
    }
}
