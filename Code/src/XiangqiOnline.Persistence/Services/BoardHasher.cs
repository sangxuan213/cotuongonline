using XiangqiOnline.RuleEngine.Adjudication;
using XiangqiOnline.RuleEngine.Models;

namespace XiangqiOnline.Persistence.Services;

/// <summary>
/// Tính board hash (SHA-256) từ trạng thái bàn cờ. Dùng để ghi board_hash_before / board_hash_after.
/// </summary>
public static class BoardHasher
{
    public static string Hash(BoardState board)
    {
        ArgumentNullException.ThrowIfNull(board);
        return BoardFingerprint.Hash(board);
    }
}
