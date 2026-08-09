using Microsoft.Extensions.Logging;
using XiangqiOnline.Persistence.Configuration;
using XiangqiOnline.Persistence.Models;
using XiangqiOnline.Persistence.Repositories;
using XiangqiOnline.RuleEngine.Models;
using XiangqiOnline.RuleEngine.Pipeline;
using XiangqiOnline.Shared.Enums;
using XiangqiOnline.Shared.Models;

namespace XiangqiOnline.Persistence.Services;

/// <summary>
/// Dich vu commit nuoc di theo mo hinh PERSIST_FIRST:
/// 1. Validate nuoc di (authoritative board state).
/// 2. Neu hop le -> persist nuoc di TRUOC, trong mot transaction atomic.
/// 3. Cap nhat board state (turn/revision/board_hash) SONG SONG trong cung transaction.
/// 4. Neu DB fail -> toan bo transaction rollback -> khong co partial state.
/// 5. Duplicate clientMoveId -> tra ve Duplicate, khong ghi them row.
/// </summary>
public sealed class MoveCommittingService
{
    private readonly DatabaseOptions _options;
    private readonly MoveValidationPipeline _validationPipeline;
    private readonly ILogger<MoveCommittingService> _logger;

    public MoveCommittingService(
        DatabaseOptions options,
        MoveValidationPipeline validationPipeline,
        ILogger<MoveCommittingService> logger)
    {
        _options = options;
        _validationPipeline = validationPipeline;
        _logger = logger;
    }

    /// <summary>
    /// Commit mot nuoc di. Khong nem exception cho loi nghiep vu/persistence
    /// (tra ve ket qua voi status tuong ung). Logging khong lam crash luong nghiep vu.
    /// </summary>
    public MoveCommitResult Commit(MatchRecord match, BoardState board, MoveIntent intent)
    {
        try
        {
            // 0. Kiem tra nuoc di hop le
            var validation = _validationPipeline.Validate(board, intent);
            if (!validation.IsValid)
            {
                _logger.LogInformation("Move rejected. matchId={MatchId} clientMoveId={ClientMoveId} error={ErrorCode}",
                    match.MatchId, intent.ClientMoveId, validation.ErrorCode);
                return new MoveCommitResult(MoveCommitStatus.Rejected, ErrorCode: validation.ErrorCode, Message: validation.Message);
            }

            // 1. Chuan bi du lieu truoc khi persist
            var boardBefore = board;
            var boardAfter = board.ApplyMove(intent.From, intent.To);
            var hashBefore = BoardHasher.Hash(boardBefore);
            var hashAfter = BoardHasher.Hash(boardAfter);
            var nextRevision = match.Revision + 1;
            var moveNumber = (int)(match.Revision + 1); // move_number based on revision

            var movingPiece = board.GetPieceAt(intent.From);
            var capturedPiece = board.GetPieceAt(intent.To);
            var move = new MoveRecord(
                MoveId: IdGenerator.NewUlid(),
                MatchId: match.MatchId,
                ClientMoveId: intent.ClientMoveId,
                PieceId: movingPiece!.Id,
                From: intent.From,
                To: intent.To,
                CapturedPieceId: capturedPiece?.Id,
                BoardHashBefore: hashBefore,
                BoardHashAfter: hashAfter,
                MoveNumber: moveNumber,
                Result: "COMMITTED",
                CreatedAtUtc: DateTime.UtcNow);

            // 2. Persist trong transaction atomic
            var committed = PersistAtomic(match, move, boardAfter, nextRevision);
            if (!committed)
            {
                return new MoveCommitResult(MoveCommitStatus.Duplicate, ErrorCode: "DUPLICATE_MOVE", Message: "Nuoc di trung lap.");
            }

            _logger.LogInformation(
                "Move committed. matchId={MatchId} clientMoveId={ClientMoveId} moveNumber={MoveNumber} revision={Revision} hash={HashAfter}",
                match.MatchId, intent.ClientMoveId, moveNumber, nextRevision, hashAfter);

            return new MoveCommitResult(MoveCommitStatus.Committed, move, nextRevision);
        }
        catch (Exception ex)
        {
            // Persistence failure -> rollback da xu ly trong PersistAtomic;
            // day chi log va tra ve PersistenceFailure, khong crash luong nghiep vu.
            _logger.LogError(ex, "Persistence failure during move commit. matchId={MatchId} clientMoveId={ClientMoveId}",
                match.MatchId, intent.ClientMoveId);
            return new MoveCommitResult(MoveCommitStatus.PersistenceFailure, ErrorCode: "PERSISTENCE_FAILURE", Message: ex.Message);
        }
    }

    /// <summary>
    /// Thuc hien insert move + update match state trong MOT transaction.
    /// Tra ve false neu duplicate (unique constraint). Nem exception neu loi khac -> rollback.
    /// </summary>
    private bool PersistAtomic(MatchRecord match, MoveRecord move, BoardState boardAfter, long nextRevision)
    {
        using var connection = new Microsoft.Data.Sqlite.SqliteConnection(_options.BuildConnectionString());
        connection.Open();
        using var transaction = connection.BeginTransaction();

        try
        {
            var moveRepo = new MoveRepository(connection, CreateLogger<MoveRepository>());
            var matchRepo = new MatchRepository(connection, CreateLogger<MatchRepository>());

            // 3. Persist move truoc (PERSIST_FIRST) - duplicate protection qua unique constraint
            var inserted = moveRepo.TryInsert(move);
            if (!inserted)
            {
                transaction.Rollback();
                return false;
            }

            // 4. Cap nhat board state trong cung transaction
            matchRepo.UpdateBoardState(
                match.MatchId,
                boardAfter.Turn == SideColor.Red ? "RED" : "BLACK",
                nextRevision,
                move.BoardHashAfter);

            transaction.Commit();
            return true;
        }
        catch
        {
            try { transaction.Rollback(); }
            catch { /* best effort */ }
            throw;
        }
    }

    private ILogger<T> CreateLogger<T>()
    {
        return Microsoft.Extensions.Logging.Abstractions.NullLogger<T>.Instance;
    }
}
