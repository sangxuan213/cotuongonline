using Microsoft.Data.Sqlite;
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
/// Dịch vụ commit nước đi theo mô hình PERSIST_FIRST tuân thủ DDL UDM18_Database_Schema_v1.1.sql:
/// 1. Validate nước đi (authoritative board state).
/// 2. Nếu hợp lệ -> persist nước đi TRƯỚC trong một transaction atomic.
/// 3. Cập nhật board state (total_moves, final_revision, position_history) trong CÙNG transaction.
/// 4. Nếu DB fail -> toàn bộ transaction rollback -> không có partial state.
/// 5. Duplicate clientMoveId -> trả về Duplicate, không ghi thêm row.
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
    /// Commit một nước đi. Không ném exception cho lỗi nghiệp vụ/persistence.
    /// Logging không làm crash luồng nghiệp vụ.
    /// </summary>
    public MoveCommitResult Commit(
        MatchRecord match,
        BoardState board,
        MoveIntent intent,
        int redRemainingMs = 600000,
        int blackRemainingMs = 600000)
    {
        try
        {
            // 0. Kiểm tra nước đi hợp lệ
            var validation = _validationPipeline.Validate(board, intent);
            if (!validation.IsValid)
            {
                _logger.LogInformation("Move rejected. matchId={MatchId} clientMoveId={ClientMoveId} error={ErrorCode}",
                    match.MatchId, intent.ClientMoveId, validation.ErrorCode);
                return new MoveCommitResult(MoveCommitStatus.Rejected, ErrorCode: validation.ErrorCode, Message: validation.Message);
            }

            // 1. Chuẩn bị dữ liệu trước khi persist
            var boardBefore = board;
            var boardAfter = board.ApplyMove(intent.From, intent.To);
            var hashBefore = BoardHasher.Hash(boardBefore);
            var hashAfter = BoardHasher.Hash(boardAfter);
            var nextRevision = (match.FinalRevision ?? 0) + 1;
            var moveIndex = match.TotalMoves + 1;

            var movingPiece = board.GetPieceAt(intent.From);
            var capturedPiece = board.GetPieceAt(intent.To);
            var pieceType = movingPiece!.Type.ToString().ToUpperInvariant();
            var side = movingPiece.Side == SideColor.Red ? "RED" : "BLACK";

            var isCapture = capturedPiece != null ? 1 : 0;
            var isCheck = validation.IsCheck ? 1 : 0;
            var isCheckmate = validation.IsCheckmate ? 1 : 0;

            var moveClass = isCheckmate == 1 || isCheck == 1 ? "CHECK" : (isCapture == 1 ? "KILL" : "IDLE");

            var move = new MoveRecord(
                MoveId: IdGenerator.NewUlid(),
                ClientMoveId: intent.ClientMoveId,
                MatchId: match.MatchId,
                MoveIndex: moveIndex,
                Revision: nextRevision,
                Side: side,
                PieceId: movingPiece.Id,
                PieceType: pieceType,
                From: intent.From,
                To: intent.To,
                CapturedPieceId: capturedPiece?.Id,
                MoveClass: moveClass,
                ClassificationFactsJson: "{}",
                IsCapture: isCapture,
                IsCheck: isCheck,
                IsCheckmate: isCheckmate,
                RedRemainingMs: Math.Max(0, redRemainingMs),
                BlackRemainingMs: Math.Max(0, blackRemainingMs),
                BoardHashBefore: hashBefore,
                BoardHashAfter: hashAfter,
                CreatedAtUtc: DateTime.UtcNow);

            var positionHistory = new PositionHistoryRecord(
                MatchId: match.MatchId,
                Revision: nextRevision,
                BoardHash: hashAfter,
                CanonicalPieceMapJson: BoardHasher.Hash(boardAfter),
                SideToMove: boardAfter.Turn == SideColor.Red ? "RED" : "BLACK",
                MoveClass: moveClass,
                ClassificationFactsJson: "{}",
                CreatedAtUtc: DateTime.UtcNow);

            // 2. Persist trong transaction atomic
            var committed = PersistAtomic(match, move, positionHistory, nextRevision, moveIndex);
            if (!committed)
            {
                return new MoveCommitResult(MoveCommitStatus.Duplicate, ErrorCode: "DUPLICATE_MOVE", Message: "Nước đi trùng lặp.");
            }

            _logger.LogInformation(
                "Move committed. matchId={MatchId} clientMoveId={ClientMoveId} moveIndex={MoveIndex} revision={Revision} hash={HashAfter}",
                match.MatchId, intent.ClientMoveId, moveIndex, nextRevision, hashAfter);

            return new MoveCommitResult(MoveCommitStatus.Committed, move, nextRevision);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Persistence failure during move commit. matchId={MatchId} clientMoveId={ClientMoveId}",
                match.MatchId, intent.ClientMoveId);
            return new MoveCommitResult(MoveCommitStatus.PersistenceFailure, ErrorCode: "PERSISTENCE_FAILURE", Message: ex.Message);
        }
    }

    /// <summary>
    /// Thực hiện insert move + position_history + update match state trong MỘT transaction.
    /// Trả về false nếu duplicate (unique constraint). Ném exception nếu lỗi khác -> rollback.
    /// </summary>
    private bool PersistAtomic(
        MatchRecord match,
        MoveRecord move,
        PositionHistoryRecord positionHistory,
        long nextRevision,
        int totalMoves)
    {
        using var connection = new SqliteConnection(_options.BuildConnectionString());
        connection.Open();
        using var transaction = connection.BeginTransaction();

        try
        {
            var moveRepo = new MoveRepository(connection, CreateLogger<MoveRepository>());
            var historyRepo = new PositionHistoryRepository(connection, CreateLogger<PositionHistoryRepository>());
            var matchRepo = new MatchRepository(connection, CreateLogger<MatchRepository>());

            // Persist move trước (PERSIST_FIRST) - duplicate protection qua unique constraint
            var inserted = moveRepo.TryInsert(move);
            if (!inserted)
            {
                transaction.Rollback();
                return false;
            }

            // Ghi position_history
            historyRepo.Insert(positionHistory);

            // Cập nhật match revision/total_moves trong cùng transaction
            matchRepo.UpdateBoardState(match.MatchId, nextRevision, totalMoves);

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

    private static ILogger<T> CreateLogger<T>()
    {
        return Microsoft.Extensions.Logging.Abstractions.NullLogger<T>.Instance;
    }
}
