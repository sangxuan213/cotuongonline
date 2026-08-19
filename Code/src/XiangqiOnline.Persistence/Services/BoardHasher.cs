using System.Security.Cryptography;
using System.Text;
using XiangqiOnline.RuleEngine.Models;

namespace XiangqiOnline.Persistence.Services;

/// <summary>
/// Tính board hash (SHA-256) từ trạng thái bàn cờ. Dùng để ghi board_hash_before / board_hash_after.
/// </summary>
public static class BoardHasher
{
    public static string Hash(BoardState board)
    {
        var sb = new StringBuilder();
        sb.Append(board.Turn.ToString()).Append('|');

        foreach (var piece in board.GetActivePieces().OrderBy(p => p.Position.X).ThenBy(p => p.Position.Y))
        {
            sb.Append(piece.Id).Append(':')
              .Append(piece.Position.X).Append(',').Append(piece.Position.Y).Append(';');
        }

        using var sha = SHA256.Create();
        var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString()));
        return Convert.ToHexStringLower(bytes);
    }
}
