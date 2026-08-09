using XiangqiOnline.Persistence.Models;

namespace XiangqiOnline.Persistence.Repositories;

/// <summary>
/// Repository quản lý bản ghi trận đấu (matches). Dùng parameterized SQL.
/// </summary>
public interface IMatchRepository
{
    /// <summary>Tạo trận đấu mới và trả về bản ghi đã lưu.</summary>
    MatchRecord Create(string matchId, string? whitePlayerId = null, string? blackPlayerId = null);

    /// <summary>Lấy thông tin trận đấu theo matchId; null nếu không tồn tại.</summary>
    MatchRecord? Get(string matchId);

    /// <summary>
    /// Cập nhật turn/revision/board_hash của trận. Chỉ dùng trong transaction commit để đảm bảo atomic.
    /// </summary>
    void UpdateBoardState(string matchId, string currentTurn, long revision, string boardHash);
}
