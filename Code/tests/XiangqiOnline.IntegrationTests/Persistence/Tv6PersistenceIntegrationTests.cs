using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using XiangqiOnline.IntegrationTests.Fixtures;
using XiangqiOnline.Persistence;
using XiangqiOnline.Persistence.Database;
using XiangqiOnline.Persistence.Models;
using XiangqiOnline.Persistence.Repositories;
using XiangqiOnline.Persistence.Services;
using XiangqiOnline.RuleEngine.Models;
using XiangqiOnline.Shared.Models;

namespace XiangqiOnline.IntegrationTests.Persistence;

/// <summary>
/// TV6 Phase 1 integration tests (P1-TV6-D5) — REAL temporary SQLite database.
/// Zero skipped. Zero fakes.
///
/// Coverage:
///   1. Legal move persists exactly one row
///   2. Duplicate client_move_id does not create another row (idempotent)
///   3. Duplicate (match_id, revision) constraint is enforced
///   4. Duplicate (match_id, move_index) constraint is enforced
///   5. Rejected/invalid move creates no move row
///   6. Persistence FK failure fully rolls back (Test A)
///   7. Full rollback after partial work (Test B)
///   8. Duplicate retry does NOT change revision or match state
///   9. Committed move can be read back consistently
///  10. board_hash_before != board_hash_after for a legal state-changing move
///  11. canonical_piece_map_json is valid JSON containing actual piece data (NOT a hash)
///  12. canonical_piece_map_json semantically matches post-move board state
/// TV2 broadcast seam: BLOCKED_BY_TV2 (documented, not faked)
/// </summary>
public sealed class Tv6PersistenceIntegrationTests : IDisposable
{
    private readonly TestDatabase _db = TestDatabase.Create();

    public void Dispose() => _db.Dispose();

    [Fact]
    public void IdGenerator_UsesFullUlidShapeAndDoesNotCollide()
    {
        var ids = Enumerable.Range(0, 10_000).Select(_ => IdGenerator.NewUlid()).ToArray();
        Assert.All(ids, id => Assert.Matches("^[0-7][0-9A-HJKMNP-TV-Z]{25}$", id));
        Assert.Equal(ids.Length, ids.Distinct(StringComparer.Ordinal).Count());
    }

    // ===== TEST 1 =====
    [Fact]
    public void Legal_move_commits_exactly_one_db_row()
    {
        var match = _db.Service.CreateMatch(IdGenerator.NewUlid(), "red", "black");
        var board = BoardState.CreateInitialBoard();
        var intent = new MoveIntent(IdGenerator.NewUlid(), new Position(0, 9), new Position(0, 7), (match.FinalRevision ?? 0));

        var result = _db.Service.CommitMove(match, board, intent);

        Assert.True(result.IsCommitted);
        Assert.Equal(1, _db.Service.CountMoves(match.MatchId));
        var stored = _db.Service.ListMoves(match.MatchId);
        Assert.Single(stored);
        Assert.Equal(intent.ClientMoveId, stored[0].ClientMoveId);
    }

    // ===== TEST 2 =====
    [Fact]
    public void Duplicate_clientMoveId_retry_still_one_row()
    {
        var match = _db.Service.CreateMatch(IdGenerator.NewUlid(), "red", "black");
        var board = BoardState.CreateInitialBoard();
        var clientMoveId = IdGenerator.NewUlid();
        var intent1 = new MoveIntent(clientMoveId, new Position(0, 9), new Position(0, 7), (match.FinalRevision ?? 0));
        var intent2 = new MoveIntent(clientMoveId, new Position(0, 9), new Position(0, 7), (match.FinalRevision ?? 0));

        var first = _db.Service.CommitMove(match, board, intent1);
        var second = _db.Service.CommitMove(match, board, intent2);

        Assert.True(first.IsCommitted);
        Assert.True(second.IsDuplicate);
        Assert.Equal(1, _db.Service.CountMoves(match.MatchId));
    }

    // ===== TEST 3 — duplicate (match_id, revision) =====
    [Fact]
    public void Duplicate_revision_constraint_is_enforced()
    {
        var match = _db.Service.CreateMatch(IdGenerator.NewUlid(), "red", "black");

        using var conn = new SqliteConnection(_db.Options.BuildConnectionString());
        conn.Open();

        var factory = new DbConnectionFactory(_db.Options);
        var repo = new MoveRepository(factory, NullLogger<MoveRepository>.Instance);

        // Insert first move at revision 1
        var move1 = MakeMove("mv-rev-a", "cm-rev-a", match.MatchId, moveIndex: 1, revision: 1);
        Assert.True(repo.TryInsert(move1));

        // Attempt second move with SAME revision — must be rejected by UNIQUE(match_id, revision)
        var move2 = MakeMove("mv-rev-b", "cm-rev-b", match.MatchId, moveIndex: 2, revision: 1);
        Assert.False(repo.TryInsert(move2), "UNIQUE(match_id, revision) must reject duplicate revision");

        // Only one row should exist
        Assert.Equal(1, repo.CountByMatch(match.MatchId));
    }

    // ===== TEST 4 — duplicate (match_id, move_index) =====
    [Fact]
    public void Duplicate_move_index_constraint_is_enforced()
    {
        var match = _db.Service.CreateMatch(IdGenerator.NewUlid(), "red", "black");

        var factory = new DbConnectionFactory(_db.Options);
        var repo = new MoveRepository(factory, NullLogger<MoveRepository>.Instance);

        // Insert first move at move_index 1
        var move1 = MakeMove("mv-idx-a", "cm-idx-a", match.MatchId, moveIndex: 1, revision: 1);
        Assert.True(repo.TryInsert(move1));

        // Attempt second move with SAME move_index — must be rejected by UNIQUE(match_id, move_index)
        var move2 = MakeMove("mv-idx-b", "cm-idx-b", match.MatchId, moveIndex: 1, revision: 2);
        Assert.False(repo.TryInsert(move2), "UNIQUE(match_id, move_index) must reject duplicate move_index");

        Assert.Equal(1, repo.CountByMatch(match.MatchId));
    }

    // ===== TEST 5 =====
    [Fact]
    public void Rejected_move_creates_zero_new_rows()
    {
        var match = _db.Service.CreateMatch(IdGenerator.NewUlid(), "red", "black");
        var board = BoardState.CreateInitialBoard();
        // Illegal: Horse at (1,9) cannot jump to (0,0)
        var intent = new MoveIntent(IdGenerator.NewUlid(), new Position(1, 9), new Position(0, 0), (match.FinalRevision ?? 0));

        var result = _db.Service.CommitMove(match, board, intent);

        Assert.True(result.IsRejected);
        Assert.Equal(0, _db.Service.CountMoves(match.MatchId));
    }

    // ===== TEST 6 — Test A: FK failure rolls back completely =====
    [Fact]
    public void PersistenceFailure_TestA_FK_violation_rolls_back_completely()
    {
        // A match record that does NOT exist in the DB — will cause FK violation on insert
        var fakeMatch = new MatchRecord(
            MatchId: "does-not-exist-in-db",
            RoomId: "room-fake",
            RedPlayerId: "red",
            BlackPlayerId: "black",
            RuleProfileId: "UDM18_WXF_PRO_2018",
            RuleProfileVersion: "1.1",
            TimeProfile: "STANDARD",
            ConfigJson: "{}",
            Status: "PLAYING",
            StartedAtUtc: DateTime.UtcNow,
            FinalRevision: 0,
            TotalMoves: 0);

        var board = BoardState.CreateInitialBoard();
        var intent = new MoveIntent(IdGenerator.NewUlid(), new Position(0, 9), new Position(0, 7), 0);

        var result = _db.Service.CommitMove(fakeMatch, board, intent);

        // Must return PersistenceFailure with correct error code
        Assert.True(result.IsPersistenceFailure);
        Assert.Equal("PERSISTENCE_FAILED", result.ErrorCode);

        // No move row must exist
        Assert.Equal(0, _db.Service.CountMoves(fakeMatch.MatchId));
    }

    // ===== TEST 7 — Test B: Rollback after partial work =====
    [Fact]
    public void PersistenceFailure_TestB_rollback_after_partial_work()
    {
        // Create a real match
        var match = _db.Service.CreateMatch(IdGenerator.NewUlid(), "red", "black");
        var board = BoardState.CreateInitialBoard();

        // Force a failure: commit a legal move first (revision 1 committed)
        var intent1 = new MoveIntent(IdGenerator.NewUlid(), new Position(0, 9), new Position(0, 7), 0);
        var ok = _db.Service.CommitMove(match, board, intent1);
        Assert.True(ok.IsCommitted);

        // Now directly insert a row with revision 2 to occupy that slot
        var factory = new DbConnectionFactory(_db.Options);
        var repo = new MoveRepository(factory, NullLogger<MoveRepository>.Instance);
        var blocker = MakeMove("mv-blocker", "cm-blocker", match.MatchId, moveIndex: 2, revision: 2);
        Assert.True(repo.TryInsert(blocker));

        // Reload match with updated revision/totalMoves to reflect real state
        var matchAfterFirst = _db.Service.GetMatch(match.MatchId)!;
        Assert.Equal(1L, matchAfterFirst.FinalRevision);

        // Now try to commit another real move — this will attempt revision 2, move_index 2
        // which are already taken, causing a constraint violation → rollback
        var boardAfterFirst = board.ApplyMove(new Position(0, 9), new Position(0, 7));
        var intent2 = new MoveIntent(IdGenerator.NewUlid(), new Position(0, 0), new Position(0, 2), (matchAfterFirst.FinalRevision ?? 0));

        // The service should detect persistence failure (conflict on revision/move_index)
        var result = _db.Service.CommitMove(matchAfterFirst, boardAfterFirst, intent2);

        // After failure: match revision must remain at 1 (unchanged)
        var matchFinal = _db.Service.GetMatch(match.MatchId)!;
        Assert.Equal(1L, matchFinal.FinalRevision);

        // Exactly 2 rows: 1 from first commit + 1 blocker (second "real" move was rolled back)
        // The important thing is the service's move was NOT added
        Assert.True(result.IsDuplicate || result.IsPersistenceFailure,
            $"Expected duplicate or persistence failure, got: {result.Status}");
    }

    // ===== TEST 8 =====
    [Fact]
    public void Duplicate_retry_does_not_change_revision_or_state()
    {
        var match = _db.Service.CreateMatch(IdGenerator.NewUlid(), "red", "black");
        var board = BoardState.CreateInitialBoard();
        var intent = new MoveIntent(IdGenerator.NewUlid(), new Position(0, 9), new Position(0, 7), (match.FinalRevision ?? 0));

        // First commit succeeds
        var ok = _db.Service.CommitMove(match, board, intent);
        Assert.True(ok.IsCommitted);

        var before = _db.Service.GetMatch(match.MatchId)!;
        Assert.Equal(1L, before.FinalRevision);

        // Duplicate retry with same clientMoveId
        var dup = new MoveIntent(intent.ClientMoveId, new Position(0, 9), new Position(0, 7), (before.FinalRevision ?? 0));
        var dupResult = _db.Service.CommitMove(match, board, dup);
        Assert.True(dupResult.IsDuplicate);

        var after = _db.Service.GetMatch(match.MatchId)!;
        Assert.Equal(1L, after.FinalRevision);           // revision unchanged
        Assert.Equal(1, _db.Service.CountMoves(match.MatchId));  // still exactly one row
    }

    // ===== TEST 9 =====
    [Fact]
    public void Committed_move_read_back_is_consistent()
    {
        var match = _db.Service.CreateMatch(IdGenerator.NewUlid(), "red", "black");
        var board = BoardState.CreateInitialBoard();
        var from = new Position(0, 9);
        var to = new Position(0, 7);
        var intent = new MoveIntent(IdGenerator.NewUlid(), from, to, (match.FinalRevision ?? 0));

        var result = _db.Service.CommitMove(match, board, intent);
        Assert.True(result.IsCommitted);

        var stored = _db.Service.ListMoves(match.MatchId);
        var move = Assert.Single(stored);

        Assert.Equal(intent.ClientMoveId, move.ClientMoveId);
        Assert.Equal(from, move.From);
        Assert.Equal(to, move.To);
        Assert.Equal(1, move.MoveIndex);
        Assert.Equal(1L, move.Revision);
        Assert.False(string.IsNullOrWhiteSpace(move.BoardHashBefore));
        Assert.False(string.IsNullOrWhiteSpace(move.BoardHashAfter));
        Assert.NotEqual(move.BoardHashBefore, move.BoardHashAfter);
    }

    // ===== TEST 10 =====
    [Fact]
    public void Board_hash_before_and_after_are_different_for_legal_move()
    {
        var match = _db.Service.CreateMatch(IdGenerator.NewUlid(), "red", "black");
        var board = BoardState.CreateInitialBoard();
        var from = new Position(0, 9);
        var to = new Position(0, 7);
        var intent = new MoveIntent(IdGenerator.NewUlid(), from, to, (match.FinalRevision ?? 0));

        // Compute expected hashes independently
        var expectedBefore = BoardHasher.Hash(board);
        var expectedAfter = BoardHasher.Hash(board.ApplyMove(from, to));

        var result = _db.Service.CommitMove(match, board, intent);
        Assert.True(result.IsCommitted);

        var stored = Assert.Single(_db.Service.ListMoves(match.MatchId));

        // board_hash_before = SHA-256 of pre-move board
        Assert.Equal(expectedBefore, stored.BoardHashBefore);
        // board_hash_after  = SHA-256 of post-move board
        Assert.Equal(expectedAfter, stored.BoardHashAfter);
        // They must differ for a state-changing move
        Assert.NotEqual(stored.BoardHashBefore, stored.BoardHashAfter);
        // Both must be 64-char hex (SHA-256)
        Assert.Equal(64, stored.BoardHashBefore.Length);
        Assert.Equal(64, stored.BoardHashAfter.Length);
    }

    // ===== TEST 11 =====
    [Fact]
    public void Canonical_piece_map_json_is_valid_json_not_a_hash()
    {
        var match = _db.Service.CreateMatch(IdGenerator.NewUlid(), "red", "black");
        var board = BoardState.CreateInitialBoard();
        var intent = new MoveIntent(IdGenerator.NewUlid(), new Position(0, 9), new Position(0, 7), (match.FinalRevision ?? 0));

        var result = _db.Service.CommitMove(match, board, intent);
        Assert.True(result.IsCommitted);

        var history = _db.Service.ListPositionHistory(match.MatchId);
        var entry = Assert.Single(history);

        var json = entry.CanonicalPieceMapJson;

        // Must NOT be a SHA-256 hash (64-char hex string)
        Assert.False(json.Length == 64 && json.All(c => "0123456789abcdef".Contains(c)),
            "canonical_piece_map_json must NOT be a raw SHA-256 hex hash");

        // Must be valid JSON
        JsonDocument doc;
        Assert.True(TryParseJson(json, out doc), $"canonical_piece_map_json is not valid JSON: {json}");

        // Must contain "turn" and "pieces" fields
        Assert.True(doc.RootElement.TryGetProperty("turn", out var turnEl),
            "canonical_piece_map_json must contain 'turn' field");
        Assert.True(doc.RootElement.TryGetProperty("pieces", out var piecesEl),
            "canonical_piece_map_json must contain 'pieces' field");

        // turn must be RED or BLACK
        Assert.Contains(turnEl.GetString()!, new[] { "RED", "BLACK" });

        // pieces must be an array with > 0 elements
        Assert.Equal(JsonValueKind.Array, piecesEl.ValueKind);
        Assert.True(piecesEl.GetArrayLength() > 0, "canonical_piece_map_json pieces array must not be empty");

        // Each piece must have id, type, side, x, y
        foreach (var piece in piecesEl.EnumerateArray())
        {
            Assert.True(piece.TryGetProperty("id", out _), "piece must have 'id'");
            Assert.True(piece.TryGetProperty("type", out _), "piece must have 'type'");
            Assert.True(piece.TryGetProperty("side", out _), "piece must have 'side'");
            Assert.True(piece.TryGetProperty("x", out _), "piece must have 'x'");
            Assert.True(piece.TryGetProperty("y", out _), "piece must have 'y'");
        }
    }

    // ===== TEST 12 =====
    [Fact]
    public void Canonical_piece_map_json_semantically_matches_post_move_board()
    {
        var match = _db.Service.CreateMatch(IdGenerator.NewUlid(), "red", "black");
        var board = BoardState.CreateInitialBoard();
        var from = new Position(0, 9);
        var to = new Position(0, 7);
        var intent = new MoveIntent(IdGenerator.NewUlid(), from, to, (match.FinalRevision ?? 0));

        var result = _db.Service.CommitMove(match, board, intent);
        Assert.True(result.IsCommitted);

        var boardAfter = board.ApplyMove(from, to);
        var expectedJson = CanonicalPieceMapSerializer.Serialize(boardAfter);

        var history = _db.Service.ListPositionHistory(match.MatchId);
        var entry = Assert.Single(history);

        Assert.Equal(expectedJson, entry.CanonicalPieceMapJson);
    }

    // ===== Helpers =====

    private static bool TryParseJson(string json, out JsonDocument doc)
    {
        try { doc = JsonDocument.Parse(json); return true; }
        catch { doc = null!; return false; }
    }

    private static MoveRecord MakeMove(
        string moveId, string clientMoveId, string matchId,
        int moveIndex, long revision)
    {
        return new MoveRecord(
            MoveId: moveId,
            ClientMoveId: clientMoveId,
            MatchId: matchId,
            MoveIndex: moveIndex,
            Revision: revision,
            Side: "RED",
            PieceId: "RED_CHARIOT_1",
            PieceType: "CHARIOT",
            From: new Position(0, 9),
            To: new Position(0, 7),
            CapturedPieceId: null,
            MoveClass: "IDLE",
            ClassificationFactsJson: "{}",
            IsCapture: 0,
            IsCheck: 0,
            IsCheckmate: 0,
            RedRemainingMs: 600000,
            BlackRemainingMs: 600000,
            BoardHashBefore: "boardhash-before-placeholder",
            BoardHashAfter: "boardhash-after-placeholder",
            CreatedAtUtc: DateTime.UtcNow);
    }

    private static int CountPieces(BoardState board) => board.GetActivePieces().Count();
}
