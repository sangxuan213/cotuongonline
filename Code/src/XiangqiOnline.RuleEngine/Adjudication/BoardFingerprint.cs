using System.Security.Cryptography;
using System.Text;
using XiangqiOnline.RuleEngine.Models;

namespace XiangqiOnline.RuleEngine.Adjudication;

public static class BoardFingerprint
{
    public static string Canonical(BoardState board)
    {
        ArgumentNullException.ThrowIfNull(board);
        var pieces = board.GetActivePieces()
            .OrderBy(piece => piece.Id, StringComparer.Ordinal)
            .Select(piece => $"{piece.Id}:{piece.Side}:{piece.Type}:{piece.Position.X},{piece.Position.Y}");
        return $"{board.Turn}|{string.Join(';', pieces)}";
    }

    public static string Hash(BoardState board) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Canonical(board)))).ToLowerInvariant();

    public static bool EqualsExact(BoardState left, BoardState right) =>
        string.Equals(Canonical(left), Canonical(right), StringComparison.Ordinal);
}
