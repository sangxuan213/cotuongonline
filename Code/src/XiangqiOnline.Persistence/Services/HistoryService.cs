using XiangqiOnline.Persistence.Models;

namespace XiangqiOnline.Persistence.Services;

public sealed record MatchHistoryDetail(
    MatchRecord Match,
    IReadOnlyList<MoveRecord> Moves,
    IReadOnlyList<PositionHistoryRecord> Positions);

public sealed class HistoryService
{
    private readonly GamePersistenceService _persistence;

    public HistoryService(GamePersistenceService persistence) =>
        _persistence = persistence ?? throw new ArgumentNullException(nameof(persistence));

    public IReadOnlyList<MatchRecord> List(string playerId, int limit = 100) =>
        _persistence.ListMatchesByPlayer(playerId, limit);

    public MatchHistoryDetail? GetDetail(string matchId)
    {
        var match = _persistence.GetMatch(matchId);
        return match is null ? null : new(match, _persistence.ListMoves(matchId), _persistence.ListPositionHistory(matchId));
    }

    public PositionHistoryRecord GetReplayPosition(string matchId, long revision)
    {
        var position = _persistence.ListPositionHistory(matchId).SingleOrDefault(item => item.Revision == revision);
        return position ?? throw new KeyNotFoundException($"Revision {revision} was not persisted for match {matchId}.");
    }
}
