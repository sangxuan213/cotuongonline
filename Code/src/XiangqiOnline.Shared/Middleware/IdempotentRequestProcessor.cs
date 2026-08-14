using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Concurrent;

namespace XiangqiOnline.Shared.Middleware
{
    public enum IdempotentRequestOutcome
    {
        /// <summary>Lần đầu thấy requestId này — đã thực sự chạy processAsync.</summary>
        Processed,

        /// <summary>Trùng requestId (retry) — trả lại kết quả cũ, KHÔNG chạy processAsync lần nữa.</summary>
        ReturnedCached,

        /// <summary>protocolVersion không nằm trong danh sách hỗ trợ — bị từ chối trước khi chạm tới processAsync.</summary>
        RejectedVersion
    }

    public readonly record struct IdempotentRequestResult<TResult>(IdempotentRequestOutcome Outcome, TResult? Result);

    /// <summary>
    /// P2-TV1-D2 deliverable "Idempotency middleware + version validation": bọc quanh
    /// MỘT hàm xử lý nghiệp vụ (do tầng trên — Lobby/Move/... cung cấp qua
    /// <paramref name="processAsync"/> mỗi lần gọi ProcessAsync) để đảm bảo:
    ///
    /// 1. protocolVersion sai -> từ chối ngay, không chạy nghiệp vụ (map ra
    ///    PROTOCOL_VERSION_UNSUPPORTED ở tầng gọi, TV1 không tự tạo envelope lỗi vì
    ///    hình dạng response là tuỳ domain của tầng trên).
    /// 2. requestId trùng với 1 request đã xử lý xong -> trả lại đúng kết quả cũ, không
    ///    chạy processAsync lần 2 (chống áp dụng nước đi 2 lần khi Client retry).
    /// 3. requestId trùng với 1 request ĐANG xử lý dở (race thật — 2 gói tin trùng
    ///    requestId tới gần như đồng thời, ví dụ Client retry quá sớm) -> gộp lại,
    ///    processAsync chỉ chạy đúng 1 lần, các caller còn lại chờ chung 1 kết quả.
    /// </summary>
    public sealed class IdempotentRequestProcessor<TResult>
    {
        private readonly BoundedIdempotencyCache<TResult> _cache;
        private readonly HashSet<string> _supportedProtocolVersions;
        private readonly ConcurrentDictionary<string, Lazy<Task<TResult>>> _inFlight = new();

        public IdempotentRequestProcessor(BoundedIdempotencyCache<TResult> cache, params string[] supportedProtocolVersions)
        {
            _cache = cache ?? throw new ArgumentNullException(nameof(cache));
            if (supportedProtocolVersions is null || supportedProtocolVersions.Length == 0)
                throw new ArgumentException("Phải khai báo ít nhất 1 protocol version được hỗ trợ.", nameof(supportedProtocolVersions));
            _supportedProtocolVersions = supportedProtocolVersions.ToHashSet();
        }

        public async Task<IdempotentRequestResult<TResult>> ProcessAsync(
            string requestId,
            string protocolVersion,
            Func<Task<TResult>> processAsync)
        {
            if (string.IsNullOrEmpty(requestId))
                throw new ArgumentException("requestId không được rỗng.", nameof(requestId));
            ArgumentNullException.ThrowIfNull(processAsync);

            if (!_supportedProtocolVersions.Contains(protocolVersion))
                return new IdempotentRequestResult<TResult>(IdempotentRequestOutcome.RejectedVersion, default);

            if (_cache.TryGet(requestId, out var cached))
                return new IdempotentRequestResult<TResult>(IdempotentRequestOutcome.ReturnedCached, cached);

            // Lazy<> đảm bảo processAsync chỉ THỰC SỰ chạy đúng 1 lần dù nhiều thread
            // cùng lúc GetOrAdd trùng key (đặc tính của ConcurrentDictionary.GetOrAdd là
            // factory CÓ THỂ bị gọi construct nhiều lần dưới race, nhưng chỉ Lazy nào
            // "thắng" và được lưu mới có .Value bị truy cập -> chỉ 1 lần gọi thật).
            var lazy = _inFlight.GetOrAdd(
                requestId,
                _ => new Lazy<Task<TResult>>(processAsync, LazyThreadSafetyMode.ExecutionAndPublication));

            TResult result;
            try
            {
                result = await lazy.Value.ConfigureAwait(false);
            }
            catch
            {
                // Xử lý lỗi -> không cache, dọn in-flight để lần retry thật sự tiếp theo được chạy lại từ đầu.
                _inFlight.TryRemove(requestId, out _);
                throw;
            }

            // TryRemove trên ConcurrentDictionary chỉ đúng 1 caller thành công (atomic) dù
            // bao nhiêu caller đang cùng chờ chung 1 Lazy.Value — người đó chịu trách
            // nhiệm ghi kết quả vào cache dài hạn; những người còn lại coi như "cached".
            bool isResponsibleForCaching = _inFlight.TryRemove(requestId, out _);
            if (isResponsibleForCaching)
                _cache.Set(requestId, result);

            return new IdempotentRequestResult<TResult>(
                isResponsibleForCaching ? IdempotentRequestOutcome.Processed : IdempotentRequestOutcome.ReturnedCached,
                result);
        }
    }
}
