using System;
using System.Threading;
using System.Threading.Tasks;

namespace XiangqiOnline.Shared.Transport
{
    /// <summary>
    /// P2-TV1-D1: theo dõi "còn sống về nghiệp vụ" của MỘT kết nối, độc lập với
    /// transport timeout của ConnectionReceiveLoop.
    ///
    /// Cách hoạt động: caller gọi <see cref="NotifyActivity"/> mỗi khi nhận được
    /// BẤT KỲ frame hợp lệ nào (bao gồm cả PING/PONG lẫn traffic nghiệp vụ thật) —
    /// vì vậy traffic hợp lệ tự nhiên reset đồng hồ, không bao giờ false-positive
    /// timeout chỉ vì "không thấy PING" trong khi hai bên đang bận trao đổi game.
    ///
    /// Khi rảnh quá <see cref="HeartbeatSettings.HeartbeatIntervalMs"/>, tự gửi PING
    /// (qua delegate do caller cung cấp) để chủ động dò phía kia. Khi rảnh quá
    /// <see cref="HeartbeatSettings.HeartbeatTimeoutMs"/> (không có gì hồi đáp, kể cả
    /// PING của chính mình cũng không có gì trả lời), raise <see cref="TimedOut"/> và
    /// tự dừng — caller (thường là nơi sở hữu socket) chịu trách nhiệm đóng kết nối
    /// và dọn task/resource liên quan.
    /// </summary>
    public sealed class HeartbeatMonitor : IAsyncDisposable
    {
        private readonly HeartbeatSettings _settings;
        private readonly Func<CancellationToken, Task> _sendPingAsync;
        private readonly CancellationTokenSource _cts = new();
        private long _lastActivityTicks;
        private Task? _loopTask;
        private int _stopped;  // guard chống raise TimedOut 2 lần
        private int _disposed; // guard chống Dispose logic chạy 2 lần (tách riêng _stopped)

        /// <summary>Raised đúng 1 lần khi phát hiện timeout nghiệp vụ. Không raise lại sau khi Stop/Dispose.</summary>
        public event Action? TimedOut;

        public HeartbeatMonitor(HeartbeatSettings settings, Func<CancellationToken, Task> sendPingAsync)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _settings.Validate();
            _sendPingAsync = sendPingAsync ?? throw new ArgumentNullException(nameof(sendPingAsync));
            NotifyActivity();
        }

        /// <summary>Gọi mỗi khi nhận được 1 frame hợp lệ (PING, PONG, hoặc traffic nghiệp vụ) — reset đồng hồ idle.</summary>
        public void NotifyActivity() => Interlocked.Exchange(ref _lastActivityTicks, Environment.TickCount64);

        /// <summary>Bắt đầu vòng lặp theo dõi nền. Gọi 1 lần sau khi đã đăng ký xong TimedOut.</summary>
        public void Start()
        {
            if (_loopTask is not null) throw new InvalidOperationException("HeartbeatMonitor đã Start rồi.");
            _loopTask = RunLoopAsync(_cts.Token);
        }

        private async Task RunLoopAsync(CancellationToken ct)
        {
            try
            {
                bool pingSentThisIdlePeriod = false;

                while (!ct.IsCancellationRequested)
                {
                    await Task.Delay(_settings.PollIntervalMs, ct).ConfigureAwait(false);

                    long idleMs = Environment.TickCount64 - Interlocked.Read(ref _lastActivityTicks);

                    if (idleMs >= _settings.HeartbeatTimeoutMs)
                    {
                        RaiseTimedOutOnce();
                        return;
                    }

                    if (idleMs >= _settings.HeartbeatIntervalMs)
                    {
                        // Chỉ gửi 1 lần cho mỗi giai đoạn idle liên tục — tránh spam PING
                        // mỗi vòng poll trong lúc chờ HeartbeatTimeoutMs tới.
                        if (!pingSentThisIdlePeriod)
                        {
                            pingSentThisIdlePeriod = true;
                            try
                            {
                                await _sendPingAsync(ct).ConfigureAwait(false);
                            }
                            catch (OperationCanceledException)
                            {
                                throw;
                            }
                            catch
                            {
                                // Gửi PING lỗi (vd. socket vừa đóng) -> để lần poll timeout
                                // kế tiếp tự phát hiện chết, không throw làm sập monitor loop.
                            }
                        }
                    }
                    else
                    {
                        pingSentThisIdlePeriod = false; // có activity mới -> giai đoạn idle mới bắt đầu lại từ đầu
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Dispose/Stop được gọi chủ động — không phải timeout, không raise TimedOut.
            }
        }

        private void RaiseTimedOutOnce()
        {
            if (Interlocked.Exchange(ref _stopped, 1) == 0)
                TimedOut?.Invoke();
        }

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 1)
                return; // đã dispose rồi — an toàn gọi lại nhiều lần (await using + gọi tay), không làm gì thêm

            Interlocked.Exchange(ref _stopped, 1); // chặn TimedOut raise sau khi đã chủ động dừng
            _cts.Cancel();
            if (_loopTask is not null)
            {
                try { await _loopTask.ConfigureAwait(false); }
                catch (OperationCanceledException) { }
            }
            _cts.Dispose();
        }
    }
}
