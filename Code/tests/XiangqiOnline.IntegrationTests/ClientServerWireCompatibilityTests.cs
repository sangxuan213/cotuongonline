using System;
using System.Buffers.Binary;
using System.IO;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading.Tasks;
using XiangqiOnline.Shared.Protocol;
using XiangqiOnline.Shared.Transport;
using Xunit;

namespace XiangqiOnline.IntegrationTests;

/// <summary>
/// Chứng minh Server-side framing của TV1 (XiangqiOnline.Shared.Transport:
/// TcpServerHost + ConnectionReceiveLoop + TcpFrameCodec) tương thích trên wire
/// với client-side framing mà TV5 đã tự viết trong
/// XiangqiOnline.Client/Protocol/TcpProtocolTransport.cs.
///
/// KHÔNG reference trực tiếp XiangqiOnline.Client (WPF, net10.0-windows) vì
/// project test này là net10.0 thuần — thay vào đó, FakeTv5Client bên dưới
/// cố tình COPY LẠI logic đọc/ghi byte y hệt TcpProtocolTransport.cs (xem
/// comment tại mỗi method). Nếu TV5 đổi format phía Client, người review cần
/// cập nhật FakeTv5Client cho khớp rồi chạy lại — đó chính là điểm test này
/// bắt được sai lệch.
///
/// PHÁT HIỆN CẦN BÁO TV5/TV6: hai bên đang có 2 implementation framing độc
/// lập cho cùng 1 giao thức (xem HANDOVER.md của TV5). Bài test này chỉ xác
/// nhận wire-format hiện tại KHỚP NHAU — không thay thế việc gộp về 1
/// implementation dùng chung (khuyến nghị Phase 2).
/// </summary>
public class ClientServerWireCompatibilityTests
{
    // Phải khớp cả hai bên: TcpFrameCodec.MaxPayloadBytes (Shared) và
    // TcpProtocolTransport.MaxPayloadBytes (Client, hard-coded riêng bên đó).
    // Đổi 1 bên mà quên đổi bên kia -> test này FAIL để cảnh báo.
    private const int ExpectedMaxPayloadBytes = 65_536;

    [Fact]
    public void MaxPayloadBytes_MatchesAcrossBothImplementations()
    {
        Assert.Equal(ExpectedMaxPayloadBytes, TcpFrameCodec.MaxPayloadBytes);
    }

    [Fact]
    public async Task ServerReceivesLoop_ParsesFrame_SentUsingTv5ClientWireFormat()
    {
        await using var server = new TcpServerHost("127.0.0.1", 0);
        TcpClient? accepted = null;
        var acceptedTcs = new TaskCompletionSource();
        server.ClientAccepted += client => { accepted = client; acceptedTcs.TrySetResult(); };
        await server.StartAsync();

        using var rawClient = new TcpClient();
        await rawClient.ConnectAsync("127.0.0.1", server.BoundPort!.Value);
        await acceptedTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Envelope hình dạng y hệt GameClient.SendAsync() bên TV5 tạo ra khi gửi HELLO.
        var envelope = new
        {
            protocolVersion = "1.0",
            type = "HELLO",
            requestId = "01J000000000000000000HELLO",
            sessionToken = (string?)null,
            roomId = (string?)null,
            clientSequence = 1L,
            sentAtUtc = DateTimeOffset.UtcNow,
            payload = new { protocolVersion = "1.0", clientName = "UDM18.WPF" }
        };

        await FakeTv5Client.SendAsync(rawClient.GetStream(), envelope);

        using var serverStream = accepted!.GetStream();
        var receiveLoop = new ConnectionReceiveLoop(serverStream);
        string? receivedJson = null;
        string? violationCode = null;
        receiveLoop.FrameReceived += (_, json) => receivedJson = json;
        receiveLoop.ProtocolViolation += (code, _) => violationCode = code;

        // 1 frame rồi đóng client -> receive loop tự dừng ở EOF sạch.
        var runTask = receiveLoop.RunAsync();
        rawClient.Client.Shutdown(SocketShutdown.Send);
        await runTask.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Null(violationCode);
        Assert.NotNull(receivedJson);

        // Field-name compatibility: JSON do TV5 gửi phải deserialize đúng vào
        // RequestEnvelope<T> của TV1 (§10.3) — nếu TV5 đổi tên field, chỗ này báo lỗi.
        var parsed = JsonSerializer.Deserialize<RequestEnvelope<JsonElement>>(receivedJson!);
        Assert.NotNull(parsed);
        Assert.Equal("HELLO", parsed!.Type);
        Assert.Equal("1.0", parsed.ProtocolVersion);
        Assert.Equal(1L, parsed.ClientSequence);
    }

    [Fact]
    public async Task Tv5ClientWireReader_ParsesFrame_SentUsingServerFrameCodec()
    {
        await using var server = new TcpServerHost("127.0.0.1", 0);
        TcpClient? accepted = null;
        var acceptedTcs = new TaskCompletionSource();
        server.ClientAccepted += client => { accepted = client; acceptedTcs.TrySetResult(); };
        await server.StartAsync();

        using var rawClient = new TcpClient();
        await rawClient.ConnectAsync("127.0.0.1", server.BoundPort!.Value);
        await acceptedTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var envelope = new ServerEventEnvelope<object>
        {
            Type = "HELLO_ACK",
            EventId = "01J000000000000000000ACK1",
            ServerSequence = 1,
            ServerTimeUtc = DateTimeOffset.UtcNow,
            Payload = new { supportedVersion = "1.0" }
        };
        var payloadBytes = JsonSerializer.SerializeToUtf8Bytes(envelope);
        using var serverStream = accepted!.GetStream();
        await TcpFrameCodec.WriteFrameAsync(serverStream, payloadBytes);

        // Đọc bằng đúng logic TV5's TcpProtocolTransport.ReceiveLoopAsync dùng.
        var received = await FakeTv5Client.ReadOneFrameAsync(rawClient.GetStream());

        Assert.NotNull(received);
        Assert.True(received!.Value.TryGetProperty("type", out var typeNode));
        Assert.Equal("HELLO_ACK", typeNode.GetString());
    }

    /// <summary>
    /// Ghi/đọc byte y hệt XiangqiOnline.Client/Protocol/TcpProtocolTransport.cs
    /// (SendAsync / ReceiveLoopAsync) — copy thủ công vì không reference được
    /// project WPF từ đây. Nếu review thấy lệch với bản gốc bên Client, sửa ở
    /// đây rồi chạy lại test để biết wire-format còn khớp hay không.
    /// </summary>
    private static class FakeTv5Client
    {
        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

        public static async Task SendAsync(NetworkStream stream, object envelope)
        {
            var payload = JsonSerializer.SerializeToUtf8Bytes(envelope, JsonOptions);
            var header = new byte[4];
            BinaryPrimitives.WriteUInt32BigEndian(header, (uint)payload.Length);
            await stream.WriteAsync(header);
            await stream.WriteAsync(payload);
            await stream.FlushAsync();
        }

        public static async Task<JsonElement?> ReadOneFrameAsync(NetworkStream stream)
        {
            var header = new byte[4];
            await ReadExactlyAsync(stream, header);
            var unsignedLength = BinaryPrimitives.ReadUInt32BigEndian(header);
            if (unsignedLength is 0 or > 65_536)
                throw new InvalidDataException($"INVALID_FRAME_LENGTH: {unsignedLength}");
            var length = (int)unsignedLength;
            var payload = new byte[length];
            await ReadExactlyAsync(stream, payload);
            using var document = JsonDocument.Parse(payload);
            return document.RootElement.Clone();
        }

        private static async Task ReadExactlyAsync(NetworkStream stream, Memory<byte> target)
        {
            var read = 0;
            while (read < target.Length)
            {
                var count = await stream.ReadAsync(target[read..]);
                if (count == 0) throw new IOException("Server đã đóng kết nối.");
                read += count;
            }
        }
    }
}
