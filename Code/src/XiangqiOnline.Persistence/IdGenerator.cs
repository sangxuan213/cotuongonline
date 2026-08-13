namespace XiangqiOnline.Persistence;

/// <summary>
/// Sinh định danh duy nhất (human-friendly, sortable-ish). Dùng cho move_id, history_id.
/// Không phụ thuộc thư viện ngoài.
/// </summary>
public static class IdGenerator
{
    /// <summary>ULID-like string (26 ký tự, base32 Crockford).</summary>
    public static string NewUlid()
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var tsPart = Base32Crockford(timestamp, 10);
        var rndPart = Random.Shared.Next(0, int.MaxValue);
        var rnd = Base32Crockford(rndPart, 6);
        return (tsPart + rnd).ToLowerInvariant();
    }

    private static string Base32Crockford(long value, int length)
    {
        const string alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";
        var chars = new char[length];
        for (var i = length - 1; i >= 0; i--)
        {
            chars[i] = alphabet[(int)(value & 31)];
            value >>= 5;
        }
        return new string(chars);
    }
}
