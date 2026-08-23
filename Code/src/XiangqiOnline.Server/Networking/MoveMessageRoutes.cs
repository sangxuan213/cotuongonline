using XiangqiOnline.Persistence.Services;
using XiangqiOnline.Server.Lobby;

namespace XiangqiOnline.Server.Networking;

public static class MoveMessageRoutes
{
    public static void Register(
        MessageRouter router,
        PlayerSessionDirectory players,
        ChallengeManager challenges,
        IConnectionRegistry connections,
        GamePersistenceService persistence,
        BotMoveService? bots = null)
    {
        router.Register("MOVE_REQUEST", (request, connection, ct) =>
            MoveMessageHandler.HandleAsync(request, connection, players, challenges, connections, persistence, ct, bots));
    }
}
