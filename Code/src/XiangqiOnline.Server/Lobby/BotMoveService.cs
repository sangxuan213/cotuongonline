using System.Collections.Concurrent;
using XiangqiOnline.Persistence.Services;
using XiangqiOnline.RuleEngine.Adjudication;
using XiangqiOnline.RuleEngine.Models;
using XiangqiOnline.RuleEngine.Pipeline;
using XiangqiOnline.Server.Networking;
using XiangqiOnline.Shared.Enums;
using XiangqiOnline.Shared.Models;
using XiangqiOnline.Shared.Protocol;

namespace XiangqiOnline.Server.Lobby;

public enum BotDifficulty { Easy, Medium, Hard }

public sealed class BotMoveService
{
    private readonly PlayerSessionDirectory _players;
    private readonly IConnectionRegistry _connections;
    private readonly GamePersistenceService _persistence;
    private readonly ConcurrentDictionary<string, BotDifficulty> _rooms = new(StringComparer.Ordinal);

    public BotMoveService(PlayerSessionDirectory players, IConnectionRegistry connections, GamePersistenceService persistence)
    {
        _players = players;
        _connections = connections;
        _persistence = persistence;
    }

    public void Register(GameRoom room, BotDifficulty difficulty) => _rooms[room.RoomId] = difficulty;

    public bool IsBotRoom(GameRoom room) => _rooms.ContainsKey(room.RoomId);

    public async Task PlayIfNeededAsync(GameRoom room, CancellationToken cancellationToken)
    {
        if (!_rooms.TryGetValue(room.RoomId, out var difficulty) || room.IsTerminal || room.CurrentTurn != SideColor.Black)
            return;

        await Task.Delay(difficulty switch
        {
            BotDifficulty.Easy => 650,
            BotDifficulty.Medium => 450,
            _ => 300
        }, cancellationToken).ConfigureAwait(false);

        await room.ExecuteSerializedAsync(async () =>
        {
            if (room.IsTerminal || room.CurrentTurn != SideColor.Black) return false;
            var choice = ChooseMove(room.Board, room.Revision, difficulty);
            if (choice is null)
            {
                await FinishAsync(room, new GameResult("RED_WIN", "NO_LEGAL_MOVE", SideColor.Red,
                    DateTimeOffset.UtcNow, room.Revision, "Bot has no legal move."), cancellationToken).ConfigureAwait(false);
                return false;
            }

            var before = room.Board;
            var moving = before.GetPieceAt(choice.From)!;
            var captured = before.GetPieceAt(choice.To);
            if (!room.Clock.TryPrepareMove(SideColor.Black, out var reservation, out var reservedClocks))
            {
                await FinishAsync(room, new GameResult("RED_WIN", "TIMEOUT", SideColor.Red,
                    DateTimeOffset.UtcNow, room.Revision, "Bot clock expired."), cancellationToken).ConfigureAwait(false);
                return false;
            }

            MoveCommitResult committed;
            try
            {
                var match = _persistence.GetMatch(room.RoomId) ?? _persistence.CreateMatch(
                    room.RoomId, room.RedPlayerId, room.BlackPlayerId, room.RoomId, room.RuleProfileId, room.Clock.Profile.Id);
                committed = _persistence.CommitMove(match, before, choice,
                    checked((int)Math.Min(int.MaxValue, reservedClocks.RedRemainingMs)),
                    checked((int)Math.Min(int.MaxValue, reservedClocks.BlackRemainingMs)));
            }
            catch (Exception ex)
            {
                await AbortAsync(room, $"Không thể lưu nước đi của máy: {ex.Message}", cancellationToken).ConfigureAwait(false);
                return false;
            }
            if (!committed.IsCommitted)
            {
                await AbortAsync(room,
                    $"Máy không thể hoàn tất nước đi ({committed.ErrorCode ?? "UNKNOWN"}): {committed.Message}",
                    cancellationToken).ConfigureAwait(false);
                return false;
            }

            var next = before.ApplyMove(choice.From, choice.To);
            var termination = new GameTerminationDetector().Evaluate(next);
            var application = new MoveApplicationResult(before, next, moving, captured, choice.From, choice.To,
                BoardFingerprint.Hash(before), BoardFingerprint.Hash(next), termination.IsCheck);
            var classification = new MoveClassifier().Classify(application);
            var clocks = room.Clock.CommitPreparedMove(reservation);
            var revision = room.CommitMove(next, choice.ClientMoveId, moving.Id, choice.From, choice.To,
                captured?.Id, classification, clocks, DateTimeOffset.UtcNow);

            await RoomEventBroadcaster.BroadcastAsync(room, _players, _connections, new ServerEventEnvelope<object>
            {
                Type = "MOVE_COMMITTED",
                EventId = Guid.NewGuid().ToString("N"),
                RoomId = room.RoomId,
                Revision = revision,
                ServerTimeUtc = DateTimeOffset.UtcNow,
                Payload = new
                {
                    side = "BLACK", pieceId = moving.Id,
                    from = new { x = choice.From.X, y = choice.From.Y },
                    to = new { x = choice.To.X, y = choice.To.Y },
                    capturedPieceId = captured?.Id,
                    currentTurn = room.CurrentTurn.ToString().ToUpperInvariant(),
                    moveClass = classification.Classification.ToString(),
                    isCheck = classification.IsCheck,
                    isCheckmate = termination.IsTerminal && termination.IsCheck,
                    clocks,
                    status = room.Status.ToString(),
                    botDifficulty = difficulty.ToString().ToUpperInvariant()
                }
            }, cancellationToken).ConfigureAwait(false);
            ServerConsoleLog.Success("MÁY ĐI",
                $"{difficulty.ToString().ToUpperInvariant()} • {moving.Id} {choice.From.X},{choice.From.Y} → {choice.To.X},{choice.To.Y} • rev {revision}");

            if (termination.IsTerminal)
            {
                await FinishAsync(room, new GameResult("BLACK_WIN", termination.EndReason!, SideColor.Black,
                    DateTimeOffset.UtcNow, room.Revision, "Bot completed the game."), cancellationToken).ConfigureAwait(false);
                return true;
            }

            var repetition = new RepetitionResolver().Evaluate(room.PositionHistory, room.MustVarySide);
            if (!repetition.IsCycle && room.MustVarySide is not null)
            {
                room.SetRepetitionWarning(null, null);
            }
            else if (repetition.ShouldWarn)
            {
                room.SetRepetitionWarning(repetition.MustVarySide, repetition.CycleSignature);
                await RoomEventBroadcaster.BroadcastAsync(room, _players, _connections,
                    RoomMessages.RepetitionWarning(room), cancellationToken).ConfigureAwait(false);
            }
            else if (repetition.IsTerminal)
            {
                var resultType = repetition.Winner switch
                {
                    SideColor.Red => "RED_WIN",
                    SideColor.Black => "BLACK_WIN",
                    _ => "DRAW"
                };
                await FinishAsync(room, new GameResult(resultType, repetition.EndReason!, repetition.Winner,
                    DateTimeOffset.UtcNow, room.Revision, repetition.Explanation), cancellationToken).ConfigureAwait(false);
            }
            return true;
        }, cancellationToken).ConfigureAwait(false);
    }

    public static MoveIntent? ChooseMove(BoardState board, long revision, BotDifficulty difficulty)
    {
        var legal = GenerateLegalMoves(board, revision).ToArray();
        if (legal.Length == 0) return null;
        if (difficulty == BotDifficulty.Easy) return legal[Random.Shared.Next(legal.Length)];

        var ranked = legal.Select(move => (Move: move, Score: ScoreMove(board, move, difficulty == BotDifficulty.Hard)))
            .OrderByDescending(item => item.Score)
            .ThenBy(_ => Random.Shared.Next())
            .ToArray();
        if (difficulty == BotDifficulty.Medium)
            return ranked[Random.Shared.Next(Math.Min(4, ranked.Length))].Move;
        return ranked[0].Move;
    }

    private static IEnumerable<MoveIntent> GenerateLegalMoves(BoardState board, long revision)
    {
        var pipeline = new MoveValidationPipeline();
        foreach (var piece in board.GetActivePieces(board.Turn))
        for (var y = 0; y <= 9; y++)
        for (var x = 0; x <= 8; x++)
        {
            var target = new Position(x, y);
            if (target == piece.Position) continue;
            var move = new MoveIntent($"BOT-{Guid.NewGuid():N}", piece.Position, target, revision);
            if (pipeline.Validate(board, move).IsValid) yield return move;
        }
    }

    private static int ScoreMove(BoardState board, MoveIntent move, bool lookAhead)
    {
        var captured = board.GetPieceAt(move.To);
        var next = board.ApplyMove(move.From, move.To);
        var score = captured is null ? 0 : PieceValue(captured.Type) * 12;
        var facts = new MoveClassifier().Classify(new MoveApplicationResult(
            board, next, board.GetPieceAt(move.From)!, captured, move.From, move.To,
            BoardFingerprint.Hash(board), BoardFingerprint.Hash(next), new GameTerminationDetector().Evaluate(next).IsCheck));
        if (facts.IsCheck) score += 180;
        score += Material(next, SideColor.Black) - Material(next, SideColor.Red);
        if (lookAhead)
        {
            var opponentThreat = GenerateLegalMoves(next, 0)
                .Select(reply => next.GetPieceAt(reply.To))
                .Where(piece => piece is not null)
                .Select(piece => PieceValue(piece!.Type) * 10)
                .DefaultIfEmpty(0).Max();
            score -= opponentThreat;
        }
        return score;
    }

    private static int Material(BoardState board, SideColor side) =>
        board.GetActivePieces(side).Sum(piece => PieceValue(piece.Type));

    private static int PieceValue(PieceType type) => type switch
    {
        PieceType.General => 10000,
        PieceType.Chariot => 900,
        PieceType.Cannon => 450,
        PieceType.Horse => 420,
        PieceType.Elephant or PieceType.Advisor => 220,
        _ => 100
    };

    private async Task FinishAsync(GameRoom room, GameResult result, CancellationToken cancellationToken)
    {
        if (!room.TryFinish(result)) return;
        try
        {
            if (_persistence.GetMatch(room.RoomId) is null)
                _persistence.CreateMatch(room.RoomId, room.RedPlayerId, room.BlackPlayerId, room.RoomId, room.RuleProfileId, room.Clock.Profile.Id);
            _persistence.CompleteMatch(room.RoomId, result.ResultType, result.EndReason,
                result.WinnerSide?.ToString().ToUpperInvariant(), result.FinalRevision, result.EndedAtUtc.UtcDateTime);
        }
        catch
        {
            // A storage failure must not leave the human permanently stuck in the bot room.
        }
        _players.LeaveRoom(room.RedPlayerId);
        await RoomEventBroadcaster.BroadcastAsync(room, _players, _connections, RoomMessages.GameEnded(room), cancellationToken).ConfigureAwait(false);
        _rooms.TryRemove(room.RoomId, out _);
    }

    private Task AbortAsync(GameRoom room, string explanation, CancellationToken cancellationToken) =>
        FinishAsync(room, new GameResult("ABORTED", "BOT_FAILURE", null,
            DateTimeOffset.UtcNow, room.Revision, explanation), cancellationToken);
}
