using System.Security.Cryptography;
using System.Text;

namespace XiangqiOnline.Server.Lobby;

public sealed record IssuedSessionToken(string PlainText, string Hash);

public sealed class SessionTokenService
{
    public IssuedSessionToken Issue()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        var token = Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return new(token, Hash(token));
    }

    public bool Verify(string token, string expectedHash)
    {
        if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(expectedHash)) return false;
        var actual = Convert.FromHexString(Hash(token));
        var expected = Convert.FromHexString(expectedHash);
        return actual.Length == expected.Length && CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    public static string Hash(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();
}
