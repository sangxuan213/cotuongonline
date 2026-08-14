using XiangqiOnline.Persistence.Models;

namespace XiangqiOnline.Persistence.Repositories;

/// <summary>
/// Repository quản lý nước đi (moves). Dùng parameterized SQL.
/// </summary>
public interface IMoveRepository
{
    /// <summary>
    /// Chèn một nước đi. Trả về <c>true</c> nếu chèn mới, <c>false</c> nếu trùng
    /// (match_id, client_move_id) — duplicate retry protection.
    /// </summary>
    bool TryInsert(MoveRecord move);

    /// <summary>Lấy nước đi theo (matchId, clientMoveId); null nếu không tồn tại.</summary>
    MoveRecord? GetByClientMoveId(string matchId, string clientMoveId);

    /// <summary>Đếm số nước đi đã commit của một trận đấu.</summary>
    int CountByMatch(string matchId);

    /// <summary>Lấy tất cả nước đi của một trận theo thứ tự move_number.</summary>
    IReadOnlyList<MoveRecord> ListByMatch(string matchId);
}
