using System.IO;
using System.Text;
using System.Threading.Tasks;
using XiangqiOnline.Shared.Protocol;
using XiangqiOnline.Shared.Transport;
using Xunit;

namespace XiangqiOnline.Shared.Tests.Transport;

/// <summary>
/// P1-TV1-D5 evidence: kịch bản "Client tắt giữa frame" — khác với
/// ConnectionReceiveLoopTests.RunAsync_CleanEofBetweenFrames_RaisesDisconnected_NotViolation
/// (đóng kết nối SẠCH, giữa 2 frame trọn vẹn), đây là đóng kết nối NGAY TRONG LÚC
/// đang đọc dở 1 frame (giữa header hoặc giữa payload) — phải bị coi là vi phạm
/// giao thức (INVALID_FRAME_LENGTH), không phải Disconnected bình thường.
/// </summary>
public class ConnectionReceiveLoopMidFrameDisconnectTests
{
    [Fact]
    public async Task RunAsync_ClientClosesMidHeader_RaisesProtocolViolation_NotDisconnected()
    {
        // Chỉ 2/4 byte của length header rồi EOF — mô phỏng client rớt mạng
        // hoặc chủ động đóng socket khi đang gửi dở.
        using var ms = new MemoryStream(new byte[] { 0x00, 0x00 });
        var loop = new ConnectionReceiveLoop(ms);

        string? violationCode = null;
        bool disconnected = false;
        loop.ProtocolViolation += (code, _) => violationCode = code;
        loop.Disconnected += () => disconnected = true;

        await loop.RunAsync();

        Assert.Equal("INVALID_FRAME_LENGTH", violationCode);
        Assert.False(disconnected);
    }

    [Fact]
    public async Task RunAsync_ClientClosesMidPayload_RaisesProtocolViolation_NotDisconnected()
    {
        // Header khai báo 10 byte payload nhưng chỉ gửi 3 byte rồi đóng socket.
        using var ms = new MemoryStream();
        ms.Write(new byte[] { 0x00, 0x00, 0x00, 0x0A });
        ms.Write(new byte[] { 1, 2, 3 });
        ms.Position = 0;

        var loop = new ConnectionReceiveLoop(ms);
        string? violationCode = null;
        bool disconnected = false;
        loop.ProtocolViolation += (code, _) => violationCode = code;
        loop.Disconnected += () => disconnected = true;

        await loop.RunAsync();

        Assert.Equal("INVALID_FRAME_LENGTH", violationCode);
        Assert.False(disconnected);
    }

    [Fact]
    public async Task RunAsync_OversizedPayloadDeclaredInHeader_RaisesProtocolViolation()
    {
        // Kịch bản "payload quá lớn" ở đúng tầng receive loop (Ngày 1 đã test
        // ở tầng TcpFrameCodec trực tiếp; đây là bằng chứng ở tầng loop TV1 sở hữu).
        using var ms = new MemoryStream(new byte[] { 0x00, 0x01, 0x00, 0x01 }); // 65,537 > 64 KiB
        var loop = new ConnectionReceiveLoop(ms);

        string? violationCode = null;
        loop.ProtocolViolation += (code, _) => violationCode = code;

        await loop.RunAsync();

        Assert.Equal("INVALID_FRAME_LENGTH", violationCode);
    }
}
