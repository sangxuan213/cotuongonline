using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using XiangqiOnline.Shared.Middleware;
using Xunit;

namespace XiangqiOnline.Shared.Tests.Middleware
{
    public class IdempotentRequestProcessorTests
    {
        private static IdempotentRequestProcessor<string> CreateProcessor(int maxEntries = 100, int ttlMs = 60_000) =>
            new(new BoundedIdempotencyCache<string>(new IdempotencyCacheSettings { MaxEntries = maxEntries, EntryTtlMs = ttlMs }), "1.0");

        [Fact]
        public async Task ProcessAsync_FirstCall_RunsProcessAndReturnsProcessedOutcome()
        {
            var processor = CreateProcessor();

            var result = await processor.ProcessAsync("req-1", "1.0", () => Task.FromResult("ket-qua"));

            Assert.Equal(IdempotentRequestOutcome.Processed, result.Outcome);
            Assert.Equal("ket-qua", result.Result);
        }

        [Fact]
        public async Task ProcessAsync_RetrySameRequestId_DoesNotRunProcessAgain_ReturnsCachedResult()
        {
            // Đúng tiêu chí: "Retry không áp dụng nước lần hai" — mô phỏng bằng counter,
            // giống hệt việc áp dụng 1 nước đi vào bàn cờ 2 lần nếu code sai.
            var processor = CreateProcessor();
            int applyCount = 0;

            Task<string> ApplyMove()
            {
                Interlocked.Increment(ref applyCount);
                return Task.FromResult("nuoc-di-ok");
            }

            var first = await processor.ProcessAsync("req-move-1", "1.0", ApplyMove);
            var retry = await processor.ProcessAsync("req-move-1", "1.0", ApplyMove);

            Assert.Equal(IdempotentRequestOutcome.Processed, first.Outcome);
            Assert.Equal(IdempotentRequestOutcome.ReturnedCached, retry.Outcome);
            Assert.Equal(first.Result, retry.Result);
            Assert.Equal(1, applyCount); // KHÔNG áp dụng lần 2
        }

        [Fact]
        public async Task ProcessAsync_UnsupportedProtocolVersion_RejectsWithoutRunningProcess()
        {
            var processor = CreateProcessor();
            bool processRan = false;

            var result = await processor.ProcessAsync("req-1", "0.9", () =>
            {
                processRan = true;
                return Task.FromResult("khong-nen-chay");
            });

            Assert.Equal(IdempotentRequestOutcome.RejectedVersion, result.Outcome);
            Assert.False(processRan);
        }

        [Fact]
        public async Task ProcessAsync_DifferentRequestIds_BothRunIndependently()
        {
            var processor = CreateProcessor();
            int applyCount = 0;

            Task<string> ApplyMove()
            {
                Interlocked.Increment(ref applyCount);
                return Task.FromResult("ok");
            }

            await processor.ProcessAsync("req-1", "1.0", ApplyMove);
            await processor.ProcessAsync("req-2", "1.0", ApplyMove);

            Assert.Equal(2, applyCount);
        }

        [Fact]
        public async Task ProcessAsync_ConcurrentDuplicateRequestId_ProcessRunsExactlyOnce()
        {
            // Kịch bản retry-quá-sớm: 2 request trùng requestId tới GẦN NHƯ ĐỒNG THỜI,
            // request gốc chưa xử lý xong. Cả 2 phải nhận cùng 1 kết quả, processAsync
            // chỉ chạy đúng 1 lần (không phải 2 lần rồi cache đè lên nhau).
            var processor = CreateProcessor();
            int applyCount = 0;
            var gate = new TaskCompletionSource();

            async Task<string> SlowApplyMove()
            {
                Interlocked.Increment(ref applyCount);
                await gate.Task; // giữ cho "đang xử lý dở" đủ lâu để race thật sự xảy ra
                return "ket-qua-cham";
            }

            var task1 = processor.ProcessAsync("req-race", "1.0", SlowApplyMove);
            var task2 = processor.ProcessAsync("req-race", "1.0", SlowApplyMove);

            await Task.Delay(50); // đảm bảo cả 2 đã cùng vào trạng thái "đang chờ" trước khi mở gate
            gate.SetResult();

            var results = await Task.WhenAll(task1, task2);

            Assert.Equal(1, applyCount);
            Assert.Equal("ket-qua-cham", results[0].Result);
            Assert.Equal("ket-qua-cham", results[1].Result);
            // Đúng 1 trong 2 là Processed, cái còn lại là ReturnedCached (thứ tự không cố định).
            var outcomes = new List<IdempotentRequestOutcome> { results[0].Outcome, results[1].Outcome };
            Assert.Contains(IdempotentRequestOutcome.Processed, outcomes);
            Assert.Contains(IdempotentRequestOutcome.ReturnedCached, outcomes);
        }

        [Fact]
        public async Task ProcessAsync_ProcessThrows_DoesNotCacheFailure_AllowsRealRetry()
        {
            var processor = CreateProcessor();
            int attempt = 0;

            Task<string> FlakyProcess()
            {
                attempt++;
                if (attempt == 1) throw new InvalidOperationException("lần đầu lỗi tạm thời");
                return Task.FromResult("thanh-cong-lan-2");
            }

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                processor.ProcessAsync("req-flaky", "1.0", FlakyProcess));

            // Lỗi không được cache -> retry thật sự (không phải trùng requestId đã "xử lý xong") phải chạy lại được.
            var retryResult = await processor.ProcessAsync("req-flaky", "1.0", FlakyProcess);

            Assert.Equal(IdempotentRequestOutcome.Processed, retryResult.Outcome);
            Assert.Equal("thanh-cong-lan-2", retryResult.Result);
            Assert.Equal(2, attempt);
        }
    }
}
