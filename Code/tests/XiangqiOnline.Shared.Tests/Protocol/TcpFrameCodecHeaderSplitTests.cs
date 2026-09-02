using System.Text;
using System.Threading.Tasks;
using XiangqiOnline.Shared.Protocol;
using XiangqiOnline.Shared.Tests.Transport;
using Xunit;

namespace XiangqiOnline.Shared.Tests.Protocol;

/// <summary>
/// P1-TV1-D5 evidence: kịch bản "header 1+3" nêu rõ trong kế hoạch — 4-byte
/// length prefix không nhất thiết đến trọn vẹn trong 1 lần ReadAsync, có thể
/// tới thành 1 byte rồi 3 byte tiếp theo (hoặc bất kỳ cách chia nào khác trên
/// socket thật). ReadExactlyAsync trong TcpFrameCodec phải ráp lại đúng.
/// </summary>
public class TcpFrameCodecHeaderSplitTests
{
    [Fact]
    public async Task ReadFrameAsync_HeaderSplitAsOneByteThenThreeBytes_StillParsesCorrectly()
    {
        using var ms = new System.IO.MemoryStream();
        var payload = Encoding.UTF8.GetBytes("{\"type\":\"HELLO\"}");
        await TcpFrameCodec.WriteFrameAsync(ms, payload);
        var frameBytes = ms.ToArray();

        // readSizes: [1, 3] chỉ áp cho 2 lần đọc đầu (đúng 4 byte header),
        // phần payload sau đó được ScriptedChunkStream trả về gọn 1 lần.
        var stream = new ScriptedChunkStream(frameBytes, 1, 3);

        var result = await TcpFrameCodec.ReadFrameAsync(stream);

        Assert.NotNull(result);
        Assert.Equal(payload, result);
    }

    [Fact]
    public async Task ReadFrameAsync_HeaderSplitAsThreeBytesThenOneByte_StillParsesCorrectly()
    {
        // Chiều ngược lại (3 rồi 1) — không giả định thứ tự chia cụ thể nào.
        using var ms = new System.IO.MemoryStream();
        var payload = Encoding.UTF8.GetBytes("{\"type\":\"PING\"}");
        await TcpFrameCodec.WriteFrameAsync(ms, payload);
        var frameBytes = ms.ToArray();

        var stream = new ScriptedChunkStream(frameBytes, 3, 1);

        var result = await TcpFrameCodec.ReadFrameAsync(stream);

        Assert.NotNull(result);
        Assert.Equal(payload, result);
    }
}
