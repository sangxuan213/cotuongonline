using System.Threading;

namespace XiangqiOnline.Shared.Session
{
    /// <summary>
    /// P3-TV1-D3 deliverable: "cổng gác" thứ tự áp dụng snapshot/event theo revision —
    /// KHÔNG chứa bất kỳ dữ liệu game nào (board, clocks, move history...), đó thuộc
    /// domain RuleEngine/Persistence (TV3/TV4/TV6). RevisionGate chỉ trả lời đúng 1 câu:
    /// "dữ liệu mang revision X này, có được phép áp dụng lên state hiện tại không".
    ///
    /// Dùng chung 1 gate cho CẢ snapshot lẫn event của cùng 1 kết nối/session — đó là lý
    /// do "event cũ không đè snapshot" tự động đúng: cả 2 loại cùng so sánh với 1
    /// LastAppliedRevision duy nhất.
    ///
    /// - <see cref="TryApplySnapshot"/>: chấp nhận nếu revision > LastAppliedRevision.
    ///   Snapshot luôn là TRẠNG THÁI ĐẦY ĐỦ (không phải delta) nên khi được chấp nhận,
    ///   caller phải THAY THẾ TOÀN BỘ state hiện tại bằng snapshot — đó chính là ý nghĩa
    ///   "không ghép nửa vời": không có khái niệm merge từng phần với snapshot, chỉ có
    ///   thay nguyên khối hoặc từ chối hoàn toàn.
    /// - <see cref="TryApplyEvent"/>: chỉ chấp nhận nếu revision đúng bằng
    ///   LastAppliedRevision + 1 (liền kề). Nhảy cóc (bỏ lỡ revision ở giữa) ->
    ///   GapDetected, KHÔNG áp dụng và KHÔNG nâng LastAppliedRevision — event là DELTA,
    ///   áp dụng delta khi thiếu dữ liệu ở giữa sẽ làm state sai lệch âm thầm.
    ///
    /// Thread-safe qua 1 lock — số lượng gọi trên 1 session thấp, không cần lock-free.
    /// </summary>
    public sealed class RevisionGate
    {
        private long _lastAppliedRevision;
        private readonly Lock _gate = new();

        public RevisionGate(long initialRevision = 0)
        {
            _lastAppliedRevision = initialRevision;
        }

        public long LastAppliedRevision
        {
            get { lock (_gate) return _lastAppliedRevision; }
        }

        public RevisionApplyDecision TryApplySnapshot(long revision)
        {
            lock (_gate)
            {
                if (revision <= _lastAppliedRevision)
                    return new RevisionApplyDecision(RevisionApplyOutcome.Stale, _lastAppliedRevision);

                _lastAppliedRevision = revision;
                return new RevisionApplyDecision(RevisionApplyOutcome.Applied, _lastAppliedRevision);
            }
        }

        public RevisionApplyDecision TryApplyEvent(long revision)
        {
            lock (_gate)
            {
                if (revision <= _lastAppliedRevision)
                    return new RevisionApplyDecision(RevisionApplyOutcome.Stale, _lastAppliedRevision);

                if (revision > _lastAppliedRevision + 1)
                    return new RevisionApplyDecision(RevisionApplyOutcome.GapDetected, _lastAppliedRevision);

                _lastAppliedRevision = revision;
                return new RevisionApplyDecision(RevisionApplyOutcome.Applied, _lastAppliedRevision);
            }
        }

        /// <summary>
        /// Đưa gate về trạng thái "chưa có gì" — dùng khi bắt đầu 1 phiên resync hoàn
        /// toàn mới (vd. sau khi reconnect với 1 match hoàn toàn khác).
        /// </summary>
        public void Reset(long initialRevision = 0)
        {
            lock (_gate) _lastAppliedRevision = initialRevision;
        }
    }
}
