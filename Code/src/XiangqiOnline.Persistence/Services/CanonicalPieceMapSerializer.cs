using System.Text;
using System.Text.Json;
using XiangqiOnline.RuleEngine.Models;
using XiangqiOnline.Shared.Enums;

namespace XiangqiOnline.Persistence.Services;

/// <summary>
/// Serializes a BoardState into the canonical_piece_map_json format
/// required by UDM18_Database_Schema_v1.1.sql.
///
/// Format: { "turn": "RED"|"BLACK", "pieces": [ { "id": "...", "type": "...", "side": "...", "x": N, "y": N }, ... ] }
/// Pieces are ordered deterministically: by side ASC, then type ASC, then id ASC.
/// This is the SEMANTIC piece-map, NOT the board hash.
/// </summary>
public static class CanonicalPieceMapSerializer
{
    public static string Serialize(BoardState board)
    {
        var pieces = board.GetActivePieces()
            .OrderBy(p => p.Side == SideColor.Red ? 0 : 1)
            .ThenBy(p => p.Type.ToString())
            .ThenBy(p => p.Id)
            .Select(p => new
            {
                id = p.Id,
                type = p.Type.ToString().ToUpperInvariant(),
                side = p.Side == SideColor.Red ? "RED" : "BLACK",
                x = p.Position.X,
                y = p.Position.Y
            });

        var doc = new
        {
            turn = board.Turn == SideColor.Red ? "RED" : "BLACK",
            pieces = pieces
        };

        return JsonSerializer.Serialize(doc, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.Never
        });
    }
}
