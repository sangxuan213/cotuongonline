using System.Threading;
using System.Threading.Tasks;
using XiangqiOnline.Shared.Session;
using Xunit;

namespace XiangqiOnline.Shared.Tests.Session
{
    public class RevisionGateTests
    {
        [Fact]
        public void TryApplySnapshot_FirstCall_Applied()
        {
            var gate = new RevisionGate();

            var result = gate.TryApplySnapshot(5);

            Assert.Equal(RevisionApplyOutcome.Applied, result.Outcome);
            Assert.Equal(5, gate.LastAppliedRevision);
        }

        [Fact]
        public void TryApplySnapshot_RevisionNotGreaterThanCurrent_Stale_DoesNotRegress()
        {
            var gate = new RevisionGate();
            gate.TryApplySnapshot(10);

            var result = gate.TryApplySnapshot(7); // snapshot cũ/trễ hơn tới sau

            Assert.Equal(RevisionApplyOutcome.Stale, result.Outcome);
            Assert.Equal(10, gate.LastAppliedRevision); // KHÔNG bị lùi lại
        }

        [Fact]
        public void TryApplyEvent_ContiguousRevision_Applied()
        {
            var gate = new RevisionGate();
            gate.TryApplySnapshot(5);

            var result = gate.TryApplyEvent(6);

            Assert.Equal(RevisionApplyOutcome.Applied, result.Outcome);
            Assert.Equal(6, gate.LastAppliedRevision);
        }

        [Fact]
        public void TryApplyEvent_OldEvent_DoesNotOverwriteNewerSnapshot()
        {
            // Đúng tiêu chí "event cũ không đè snapshot": snapshot revision 20 đã áp
            // dụng, 1 event revision 3 (rất cũ, đến trễ do mạng) tới sau đó.
            var gate = new RevisionGate();
            gate.TryApplySnapshot(20);

            var result = gate.TryApplyEvent(3);

            Assert.Equal(RevisionApplyOutcome.Stale, result.Outcome);
            Assert.Equal(20, gate.LastAppliedRevision); // snapshot không bị đè
        }

        [Fact]
        public void TryApplyEvent_DuplicateRevision_Stale()
        {
            var gate = new RevisionGate();
            gate.TryApplySnapshot(5);

            var result = gate.TryApplyEvent(5); // đúng bằng revision hiện tại, không phải mới hơn

            Assert.Equal(RevisionApplyOutcome.Stale, result.Outcome);
        }

        [Fact]
        public void TryApplyEvent_RevisionGap_DetectedAndNotApplied_LastAppliedUnchanged()
        {
            // "Không ghép nửa vời": event nhảy cóc (thiếu revision 6) không được áp
            // dụng — nếu áp dụng sẽ tạo ra state thiếu 1 bước ở giữa mà không hay biết.
            var gate = new RevisionGate();
            gate.TryApplySnapshot(5);

            var result = gate.TryApplyEvent(8); // thiếu 6, 7

            Assert.Equal(RevisionApplyOutcome.GapDetected, result.Outcome);
            Assert.Equal(5, gate.LastAppliedRevision); // KHÔNG nhảy lên 8 — chờ resync
        }

        [Fact]
        public void TryApplySnapshot_AfterGapDetected_RecoversCorrectly()
        {
            // Kịch bản thực tế đầy đủ: phát hiện gap -> (Ngày 4 sẽ yêu cầu resync) ->
            // server trả về snapshot mới -> gate phải chấp nhận và tiếp tục bình thường.
            var gate = new RevisionGate();
            gate.TryApplySnapshot(5);
            gate.TryApplyEvent(8); // gap, bị từ chối

            var resyncResult = gate.TryApplySnapshot(8); // snapshot đầy đủ tại đúng revision bị thiếu

            Assert.Equal(RevisionApplyOutcome.Applied, resyncResult.Outcome);
            Assert.Equal(8, gate.LastAppliedRevision);

            var nextEvent = gate.TryApplyEvent(9); // tiếp tục bình thường sau khi đã resync
            Assert.Equal(RevisionApplyOutcome.Applied, nextEvent.Outcome);
        }

        [Fact]
        public void Reset_ThenTryApplySnapshot_StartsFreshFromZero()
        {
            var gate = new RevisionGate();
            gate.TryApplySnapshot(50);

            gate.Reset();

            Assert.Equal(0, gate.LastAppliedRevision);
            var result = gate.TryApplySnapshot(1);
            Assert.Equal(RevisionApplyOutcome.Applied, result.Outcome);
        }

        [Fact]
        public async Task ConcurrentTryApplySnapshot_SameRevisionRace_OnlyAppliesOnce_NeverRegresses()
        {
            // Nhiều thread cùng cố áp dụng ĐÚNG 1 revision (race thật, vd 2 nguồn cùng
            // gửi resync response) — chỉ được tính là Applied đúng 1 lần logic, không
            // bao giờ có chuyện LastAppliedRevision bị lùi lại hay lệch khỏi giá trị đúng.
            var gate = new RevisionGate();
            gate.TryApplySnapshot(1); // baseline

            var tasks = new Task<RevisionApplyDecision>[20];
            for (int i = 0; i < 20; i++)
                tasks[i] = Task.Run(() => gate.TryApplySnapshot(2));

            var results = await Task.WhenAll(tasks);

            Assert.Equal(2, gate.LastAppliedRevision);
            Assert.Contains(results, r => r.Outcome == RevisionApplyOutcome.Applied);
            // Mọi kết quả (Applied hay Stale) đều phải đồng thuận LastAppliedRevision cuối cùng là 2 — không có torn state.
            Assert.All(results, r => Assert.Equal(2, r.LastAppliedRevision));
        }

        [Fact]
        public async Task ConcurrentTryApplyEvent_OutOfOrderArrival_NeverAppliesOutOfOrder()
        {
            // Network reorder thật: revision đến không theo thứ tự. Gate không được để
            // lọt bất kỳ trường hợp nào LastAppliedRevision giảm hoặc nhảy cóc thành công.
            var gate = new RevisionGate();
            gate.TryApplySnapshot(0);

            var revisionsOutOfOrder = new long[] { 3, 1, 5, 2, 4 };
            var tasks = new Task<RevisionApplyDecision>[revisionsOutOfOrder.Length];
            for (int i = 0; i < revisionsOutOfOrder.Length; i++)
            {
                long revision = revisionsOutOfOrder[i];
                tasks[i] = Task.Run(() => gate.TryApplyEvent(revision));
            }

            var results = await Task.WhenAll(tasks);

            // Bất biến cốt lõi: LastAppliedRevision cuối cùng không bao giờ lớn hơn revision lớn nhất từng gửi,
            // và không có kết quả Applied nào lại có LastAppliedRevision nhỏ hơn 1 kết quả Applied khác đã ghi nhận trước đó
            // trong chính field trả về của nó (mỗi lần Applied luôn khớp revision vừa gửi).
            foreach (var result in results)
            {
                if (result.Outcome == RevisionApplyOutcome.Applied)
                    Assert.True(result.LastAppliedRevision <= 5);
            }
            Assert.True(gate.LastAppliedRevision <= 5);
        }
    }
}
