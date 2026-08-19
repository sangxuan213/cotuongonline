namespace XiangqiOnline.Shared.Session
{
    public enum SessionTokenValidationOutcome
    {
        Valid,
        Invalid,
        Expired
    }

    /// <summary>Kết quả gọi <see cref="SessionTokenService.ValidateSessionToken"/>.</summary>
    public readonly record struct SessionTokenValidationResult(SessionTokenValidationOutcome Outcome, string? PlayerId)
    {
        public static SessionTokenValidationResult Valid(string playerId) => new(SessionTokenValidationOutcome.Valid, playerId);
        public static SessionTokenValidationResult Invalid() => new(SessionTokenValidationOutcome.Invalid, null);
        public static SessionTokenValidationResult Expired() => new(SessionTokenValidationOutcome.Expired, null);
    }
}
