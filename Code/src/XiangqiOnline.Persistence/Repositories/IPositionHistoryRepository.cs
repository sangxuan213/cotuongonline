using XiangqiOnline.Persistence.Models;

namespace XiangqiOnline.Persistence.Repositories;

/// <summary>
/// Repository quản lý vị trí thế cờ (position_history). Dùng parameterized SQL.
/// </summary>
public interface IPositionHistoryRepository
{
    /// <summary>Chèn một bản ghi position_history.</summary>
    void Insert(PositionHistoryRecord record);

    /// <summary>Lấy danh sách position_history của trận theo thứ tự revision.</summary>
    IReadOnlyList<PositionHistoryRecord> ListByMatch(string matchId);
}
