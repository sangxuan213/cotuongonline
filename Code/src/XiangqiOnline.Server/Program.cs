using Microsoft.Extensions.Logging;
using XiangqiOnline.Persistence;
using XiangqiOnline.Persistence.Configuration;
using XiangqiOnline.Persistence.Logging;
using XiangqiOnline.Persistence.Services;
using XiangqiOnline.RuleEngine.Models;
using XiangqiOnline.Shared.Models;

namespace XiangqiOnline.Server;

/// <summary>
/// Server entry point (TV6 Phase 1). Khoi tao persistence + logging, tao match demo
/// va commit mot nuoc di hop le de minh hoa PERSIST_FIRST.
/// </summary>
public static class Program
{
    public static int Main()
    {
        var loggerFactory = LoggingSetup.CreateLoggerFactory();
        var logger = loggerFactory.CreateLogger("XiangqiOnline.Server");
        var correlationId = CorrelationContext.NewId();

        using var scope = CorrelationContext.BeginScope(logger, correlationId);
        logger.LogInformation("Server starting. correlationId={CorrelationId}", correlationId);

        try
        {
            var options = DatabaseOptions.FromEnvironment();
            var persistence = new GamePersistenceService(options, loggerFactory);

            logger.LogInformation("Initializing database at {DbPath}", SecretRedactor.Redact(options.DatabasePath));
            persistence.InitializeDatabase();

            // Demo: tao match va commit mot nuoc di hop le
            var match = persistence.CreateMatch(IdGenerator.NewUlid(), "player-red", "player-black");
            logger.LogInformation("Match created. matchId={MatchId} revision={Revision}", match.MatchId, match.Revision);

            var board = BoardState.CreateInitialBoard();
            var intent = new MoveIntent(
                IdGenerator.NewUlid(),
                new Position(0, 9),   // RED_CHARIOT_1
                new Position(0, 7),   // tien len
                match.Revision);

            var result = persistence.CommitMove(match, board, intent);
            logger.LogInformation("Move commit status={Status} revision={Revision}",
                result.Status, result.Revision);

            var count = persistence.CountMoves(match.MatchId);
            logger.LogInformation("Move count for match={MatchId} is {Count}", match.MatchId, count);

            Console.WriteLine($"TV6 Phase 1 OK. Match={match.MatchId}, Moves={count}, CommitStatus={result.Status}");
            return 0;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Server failed during TV6 Phase 1 startup.");
            Console.Error.WriteLine($"Server failed: {SecretRedactor.Redact(ex.Message)}");
            return 1;
        }
    }
}
