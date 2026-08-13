# Framing — TcpFrameCodec (P1-TV1-D1)

## Định dạng frame (khóa theo Protocol Catalog v1.0 §10.1–10.2)

```
[Length: 4 byte, unsigned, big-endian (network byte order)][JSON payload: đúng Length byte, UTF-8]
```

- **Length prefix:** 4 byte, `uint32`, big-endian. Không đổi sang little-endian hay đổi số byte.
- **Payload:** JSON UTF-8, độ dài đúng bằng giá trị Length. Không có delimiter khác (không `\n`, không null-terminator).
- **Giới hạn payload:** tối đa 64 KiB (65536 byte). Length = 0 hoặc > 65536 → lỗi `INVALID_FRAME_LENGTH`.
- **Một writer queue mỗi socket** để tránh interleave frame khi ghi (áp dụng ở TcpServerHost/TcpClientService — Ngày 2).

## Quy trình đọc (receiver)

1. `ReadExactly(4)` — đọc đủ 4 byte length prefix.
2. Decode length; nếu `length == 0` hoặc `length > MaxPayloadBytes` → reject (`INVALID_FRAME_LENGTH`).
3. `ReadExactly(length)` — đọc đủ payload.
4. Decode UTF-8 nghiêm ngặt.
5. Parse JSON khi đã có đủ toàn bộ payload (không parse từng phần).
6. Validate envelope/schema trước khi route tiếp (bước này thuộc TCP Gateway, không thuộc codec).

## Class trong `XiangqiOnline.Shared.Protocol`

| Class | Vai trò |
|---|---|
| `TcpFrameCodec` | `WriteFrameAsync` / `ReadFrameAsync` — chỉ làm việc với `Stream` và `byte[]`, không mở socket, không parse JSON thành DTO. |
| `RequestEnvelope<TPayload>` | Envelope Client → Server, field khóa theo §10.3. |
| `ServerEventEnvelope<TPayload>` | Envelope Server → Client, field khóa theo §10.4. |
| `FrameDecodeException` / `FrameEncodeException` | Lỗi framing — Ngày 2 map sang error code `INVALID_FRAME_LENGTH` khi trả về Client. |

## Những gì KHÔNG làm ở Ngày 1

- Không mở `TcpListener`/`TcpClient` (Ngày 2 — `TcpServerHost`, `TcpClientService`).
- Không đọc IP/Port từ config (Ngày 2).
- Không xử lý heartbeat/reconnect (Ngày 2–3, Phase 2–3).
- Không tự sửa luật cờ hoặc trạng thái phòng (ranh giới TV1 theo §2.3 kế hoạch).

## Trạng thái Ngày 1

- [ ] Build project `XiangqiOnline.Shared` thành công.
- [ ] Test `TcpFrameCodecTests` pass (encode/decode một frame hợp lệ + các case lỗi).
- [ ] Không đổi field đã khóa trong baseline (length prefix 4 byte big-endian, field envelope §10.3/10.4).
