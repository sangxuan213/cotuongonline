# TV1 — Phase 2 (Connection & Session nâng cao) — Test Evidence & Handover

## Tổng quan
5 ngày Phase 2 đã hoàn thành, mở rộng module Connection & Session của Phase 1 (`XiangqiOnline.Shared.Transport`, `XiangqiOnline.Shared.Middleware`).

## Đối chiếu checklist theo kế hoạch

| Ngày | Yêu cầu | File chính | Test |
|---|---|---|---|
| 1 | Heartbeat 2 chiều, tách transport/business timeout | `HeartbeatMonitor`, `HeartbeatedConnection`, `ConnectionReceiveLoop` (mở rộng) | `HeartbeatMonitorTests`, `ConnectionReceiveLoopTransportTimeoutTests`, `HeartbeatedConnectionEndToEndTests` |
| 2 | Idempotency (requestId cache ngắn hạn), protocol version check | `BoundedIdempotencyCache`, `IdempotentRequestProcessor` | `BoundedIdempotencyCacheTests`, `IdempotentRequestProcessorTests` |
| 3 | Rate limit theo connection, cô lập lỗi từng Client | `TokenBucketRateLimiter`, `HeartbeatedConnection` (mở rộng) | `TokenBucketRateLimiterTests`, `HeartbeatedConnectionRateLimitTests`, `AcceptLoopSurvivesSpamTests` |
| 4 | Resilience khi Server ngắt, cancellation propagation, correlation id | `HeartbeatedConnection` (Faulted event, ConnectionId, SendFrameAsync liên kết cancellation) | `HeartbeatedConnectionResilienceTests` |
| 5 | Test còn thiếu (server shutdown, 2 lệnh đồng thời) + evidence | — | `ServerShutdownAndConcurrentSendTests` |

## Danh sách kịch bản bắt buộc theo kế hoạch (Ngày 5) và nơi test

- **Duplicate request** → `IdempotentRequestProcessorTests.ProcessAsync_RetrySameRequestId_DoesNotRunProcessAgain...` + kịch bản race thật `ProcessAsync_ConcurrentDuplicateRequestId_ProcessRunsExactlyOnce`
- **Version sai** → `IdempotentRequestProcessorTests.ProcessAsync_UnsupportedProtocolVersion_RejectsWithoutRunningProcess`
- **Spam** → `HeartbeatedConnectionRateLimitTests` (4 test) + `AcceptLoopSurvivesSpamTests`
- **Disconnect giữa frame** → `ConnectionReceiveLoopMidFrameDisconnectTests` (Phase 1 Ngày 5) + `HeartbeatedConnectionResilienceTests.ServerClosesSocketAbruptly...`
- **Server shutdown** → `ServerShutdownAndConcurrentSendTests.ServerStopAsync_WhileClientConnected_StopsCleanly_DoesNotHang`
- **Hai command gần đồng thời** → `ServerShutdownAndConcurrentSendTests.TwoConcurrentSendFrameAsyncCalls_BothFramesArriveIntact_NoInterleaving`

## Số lượng test protocol/fault (TV1, Phase 1 + Phase 2)

- Phase 1: 27 test (`Shared.Tests`) + 3 integration test = 30
- Phase 2: 11 (Ngày 1) + 11 (Ngày 2) + 9 (Ngày 3) + 4 (Ngày 4) + 2 (Ngày 5) = 37

**Tổng: 67 protocol/fault test** — vượt xa mốc tối thiểu 25 theo tiêu chí nghiệm thu.

## Vấn đề đã biết, cần TV6/TV5 quyết định (mang từ Phase 1 sang)

Server-side framing (`XiangqiOnline.Shared.Transport`) và Client-side framing (`XiangqiOnline.Client/Protocol/TcpProtocolTransport.cs`) vẫn là 2 implementation độc lập cho cùng 1 giao thức, đã verify wire-compatible (`ClientServerWireCompatibilityTests`), nhưng bản của TV5 chưa validate UTF-8 nghiêm ngặt và chưa có heartbeat/rate-limit như Phase 2 vừa bổ sung. Đề xuất gộp về dùng chung ở Phase 3 nếu team đồng ý.

## Không bypass test bằng retry vô hạn

Toàn bộ test dùng `Task.WhenAny(..., Task.Delay(timeout))` hoặc `WaitAsync(TimeSpan)` để có deadline rõ ràng, không có vòng lặp retry vô hạn nào che giấu flakiness — test fail thật sẽ hiện fail thật.
