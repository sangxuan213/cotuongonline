namespace XiangqiOnline.Shared.Session
{
    public enum RevisionApplyOutcome
    {
        /// <summary>revision mới hơn hẳn (hoặc kế tiếp liền, với event) — an toàn để áp dụng.</summary>
        Applied,

        /// <summary>revision <= LastAppliedRevision — dữ liệu cũ/trễ, KHÔNG được ghi đè lên state mới hơn đã có.</summary>
        Stale,

        /// <summary>Chỉ xảy ra với event: revision nhảy cóc (bỏ lỡ ít nhất 1 revision ở giữa) — KHÔNG áp dụng, phải resync (Ngày 4).</summary>
        GapDetected
    }

    /// <summary>Kết quả 1 lần gọi TryApplySnapshot/TryApplyEvent.</summary>
    public readonly record struct RevisionApplyDecision(RevisionApplyOutcome Outcome, long LastAppliedRevision);
}
