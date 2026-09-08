using System;
using System.Collections.Generic;
using System.Threading;

namespace XiangqiOnline.Shared.Middleware;

/// <summary>
/// P2-TV1-D2: cache kết quả theo requestId, có giới hạn — đúng tinh thần Technical
/// Contract §10.6 ("Server cache kết quả NGẮN HẠN để trả lại khi retry"). Giới hạn
/// theo 2 chiều độc lập:
///
/// - <see cref="IdempotencyCacheSettings.MaxEntries"/>: giới hạn SỐ LƯỢNG — khi đầy,
///   loại bỏ entry cũ nhất theo thứ tự truy cập (LRU), không bao giờ phình vô hạn dù
///   traffic lớn tới đâu.
/// - <see cref="IdempotencyCacheSettings.EntryTtlMs"/>: giới hạn THỜI GIAN — entry
///   quá cũ (dù cache chưa đầy) tự động bị coi là hết hạn, không trả lại nữa. Đây là
///   lý do gọi là "ngắn hạn": bảo vệ khỏi 1 client gửi lại request cũ sau rất lâu và
///   nhận nhầm kết quả đã lỗi thời.
///
/// Thread-safe qua 1 lock đơn giản — traffic idempotency-check tần suất thấp hơn
/// nhiều so với receive loop, không cần cấu trúc lock-free phức tạp.
/// </summary>
public sealed class BoundedIdempotencyCache<TResult>
{
    private sealed class Entry
    {
        public required TResult Result { get; init; }
        public required long ExpiresAtTicks { get; init; }
    }

    private readonly IdempotencyCacheSettings _settings;
    private readonly Dictionary<string, LinkedListNode<(string Key, Entry Value)>> _map = new();
    private readonly LinkedList<(string Key, Entry Value)> _lruOrder = new(); // đầu = ít dùng gần đây nhất, cuối = mới dùng gần đây nhất
    private readonly Lock _lock = new();

    public BoundedIdempotencyCache(IdempotencyCacheSettings settings)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _settings.Validate();
    }

    /// <summary>Số entry hiện có trong cache — chỉ để test/quan sát, không phải API nghiệp vụ.</summary>
    public int Count
    {
        get { lock (_lock) { return _map.Count; } }
    }

    public bool TryGet(string requestId, out TResult? result)
    {
        lock (_lock)
        {
            if (_map.TryGetValue(requestId, out var node))
            {
                if (node.Value.Value.ExpiresAtTicks > Environment.TickCount64)
                {
                    // Hit hợp lệ -> đưa lên cuối danh sách LRU (vừa được dùng).
                    _lruOrder.Remove(node);
                    _lruOrder.AddLast(node);
                    result = node.Value.Value.Result;
                    return true;
                }

                // Hết hạn -> dọn luôn, coi như miss.
                _lruOrder.Remove(node);
                _map.Remove(requestId);
            }

            result = default;
            return false;
        }
    }

    public void Set(string requestId, TResult result)
    {
        lock (_lock)
        {
            if (_map.TryGetValue(requestId, out var existing))
            {
                _lruOrder.Remove(existing);
                _map.Remove(requestId);
            }

            var entry = new Entry { Result = result, ExpiresAtTicks = Environment.TickCount64 + _settings.EntryTtlMs };
            var node = new LinkedListNode<(string, Entry)>((requestId, entry));
            _lruOrder.AddLast(node);
            _map[requestId] = node;

            while (_map.Count > _settings.MaxEntries)
            {
                var oldest = _lruOrder.First;
                if (oldest is null) break;
                _lruOrder.RemoveFirst();
                _map.Remove(oldest.Value.Key);
            }
        }
    }
}
