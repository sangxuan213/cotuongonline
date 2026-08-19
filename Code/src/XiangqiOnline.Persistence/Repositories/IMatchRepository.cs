using XiangqiOnline.Persistence.Models;

namespace XiangqiOnline.Persistence.Repositories;

/// <summary>
/// Repository quản lý bản ghi trận đấu (matches). Dùng parameterized SQL.
/// </summary>
public interface IMatchRepository
{
    /// <summary>Tạo trận đấu mới và trả về bản ghi đã lưu.</summary>
    MatchRecord Create(
        string matchId,
        string roomId,
        string redPlayerId,
        string blackPlayerId,
        string ruleProfileId = "UDM18_WXF_PRO_2018",
        string ruleProfileVersion = "1.1",
        string timeProfile = "STANDARD",
        string configJson = "{}");

    /// <summary>Lấy thông tin trận đấu theo matchId; null nếu không tồn tại.</summary>
    MatchRecord? Get(string matchId);

    /// <summary>
    /// Cập nhật revision và total_moves của trận trong atomic move commit transaction.
    /// </summary>
    void UpdateBoardState(string matchId, long revision, int totalMoves);

    bool Complete(
        string matchId,
        string resultType,
        string endReason,
        string? winnerSide,
        long finalRevision,
        DateTime endedAtUtc);
}
