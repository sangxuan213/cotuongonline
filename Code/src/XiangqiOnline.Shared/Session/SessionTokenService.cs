using System;
using System.Buffers.Text;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

namespace XiangqiOnline.Shared.Session;

/// <summary>
/// P3-TV1-D1 deliverable "Reconnect security contract + token service" — đúng theo
/// §15.2 (Security baseline) và §9.3 (Reconnect profile) của Technical Contract:
///
/// - Token sinh ngẫu nhiên bằng <see cref="RandomNumberGenerator"/> (CSPRNG, không
///   phải <see cref="Random"/>), tối thiểu 256 bit, encode base64url.
/// - KHÔNG BAO GIỜ lưu token dạng plaintext — chỉ lưu SHA-256 hash. Token gốc chỉ
///   tồn tại trong bộ nhớ đúng 1 lần lúc trả về cho caller lúc IssueToken, sau đó
///   service không giữ lại bản plaintext nào nữa.
/// - "Rotate khi login mới" + chính sách duplicate session: mỗi lần IssueToken cho
///   1 playerId, token CŨ (nếu có) của đúng playerId đó bị vô hiệu hoá ngay lập tức
///   — tại mọi thời điểm, 1 playerId chỉ có ĐÚNG 1 token còn hiệu lực.
/// - Cửa sổ reconnect 60s (cấu hình được qua <see cref="SessionTokenSettings"/>):
///   token hết hạn sau ReconnectWindowSeconds kể từ lúc issue hoặc lần cuối
///   <see cref="ExtendReconnectWindow"/> được gọi (thường gọi lúc phát hiện mất kết nối).
///
/// KHÔNG log token gốc ở bất kỳ đâu trong class này — chỉ log thông qua PlayerId
/// hoặc hash (không nhạy cảm) nếu tầng gọi cần audit.
/// </summary>
public sealed class SessionTokenService
{
    private sealed class TokenRecord
    {
        public required string PlayerId { get; init; }
        public required DateTimeOffset ExpiresAtUtc { get; set; }
    }

    private readonly SessionTokenSettings _settings;
    private readonly Dictionary<string, TokenRecord> _recordsByHash = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _activeHashByPlayerId = new(StringComparer.Ordinal);
    private readonly Lock _gate = new();

    public SessionTokenService(SessionTokenSettings settings)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _settings.Validate();
    }

    /// <summary>
    /// Cấp token mới cho playerId. Token CŨ của đúng playerId này (nếu có) bị vô
    /// hiệu ngay lập tức — đúng chính sách "duplicate session"/"rotate khi login mới".
    /// Giá trị <c>Token</c> trả về CHỈ xuất hiện đúng 1 lần ở đây — caller chịu trách
    /// nhiệm gửi cho đúng client qua kênh đã mã hoá/riêng tư, KHÔNG log lại.
    /// </summary>
    public IssuedSessionToken IssueToken(string playerId, DateTimeOffset nowUtc)
    {
        if (string.IsNullOrWhiteSpace(playerId))
            throw new ArgumentException("playerId không được rỗng.", nameof(playerId));

        var tokenBytes = RandomNumberGenerator.GetBytes(_settings.TokenSizeBytes);
        var token = Base64Url.EncodeToString(tokenBytes);
        var hash = ComputeHash(token);
        var expiresAt = nowUtc.AddSeconds(_settings.ReconnectWindowSeconds);

        lock (_gate)
        {
            if (_activeHashByPlayerId.TryGetValue(playerId, out var oldHash))
                _recordsByHash.Remove(oldHash); // rotate: token cũ chết ngay, không còn dùng lại được

            _recordsByHash[hash] = new TokenRecord { PlayerId = playerId, ExpiresAtUtc = expiresAt };
            _activeHashByPlayerId[playerId] = hash;
        }

        return new IssuedSessionToken(token, expiresAt);
    }

    /// <summary>
    /// Kiểm tra 1 sessionToken client gửi lên có hợp lệ không. Đây chính là
    /// "ValidateSessionToken" — mọi message có khả năng đổi state phải qua đây trước
    /// (đúng §3.1: "Mọi message có khả năng thay đổi state phải được xác thực session").
    /// </summary>
    public SessionTokenValidationResult ValidateSessionToken(string? token, DateTimeOffset nowUtc)
    {
        if (string.IsNullOrEmpty(token))
            return SessionTokenValidationResult.Invalid();

        var hash = ComputeHash(token);

        lock (_gate)
        {
            if (!_recordsByHash.TryGetValue(hash, out var record))
                return SessionTokenValidationResult.Invalid();

            if (record.ExpiresAtUtc <= nowUtc)
            {
                _recordsByHash.Remove(hash);
                _activeHashByPlayerId.Remove(record.PlayerId);
                return SessionTokenValidationResult.Expired();
            }

            return SessionTokenValidationResult.Valid(record.PlayerId);
        }
    }

    /// <summary>
    /// Gọi khi phát hiện 1 player mất kết nối (socket chết) nhưng vẫn muốn giữ token
    /// còn hiệu lực để họ reconnect trong cửa sổ 60s — đẩy lại deadline hết hạn tính
    /// từ thời điểm này, KHÔNG cấp token mới (giữ nguyên token cũ để client dùng lại).
    /// </summary>
    public bool ExtendReconnectWindow(string playerId, DateTimeOffset nowUtc)
    {
        lock (_gate)
        {
            if (!_activeHashByPlayerId.TryGetValue(playerId, out var hash) ||
                !_recordsByHash.TryGetValue(hash, out var record))
            {
                return false;
            }

            record.ExpiresAtUtc = nowUtc.AddSeconds(_settings.ReconnectWindowSeconds);
            return true;
        }
    }

    /// <summary>Thu hồi token của 1 playerId ngay lập tức (vd. logout chủ động).</summary>
    public void Revoke(string playerId)
    {
        lock (_gate)
        {
            if (_activeHashByPlayerId.Remove(playerId, out var hash))
                _recordsByHash.Remove(hash);
        }
    }

    private static string ComputeHash(string token)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(bytes);
    }
}

/// <summary>
/// Kết quả IssueToken. <see cref="Token"/> là giá trị PLAINTEXT duy nhất từng tồn tại
/// bên ngoài service — caller phải gửi đi rồi bỏ qua, không lưu/log lại chỗ khác.
/// </summary>
public readonly record struct IssuedSessionToken(string Token, DateTimeOffset ExpiresAtUtc);
