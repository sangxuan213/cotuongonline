# BÁO CÁO NGHIỆM THU ĐỒ ÁN UDM_18: CỜ TƯỚNG TRỰC TUYẾN (XIANGQI ONLINE)
**Môn học**: Lập trình Mạng / Hệ Thống Phân Tán  
**Nền tảng**: C# 13, .NET 10.0, WPF, SQLite, TCP Socket thuần  
**Repository GitHub**: [sangxuan213/cotuongonline](https://github.com/sangxuan213/cotuongonline) — Branch: `develop`

---

## 1. TỔNG QUAN KẾT QUẢ NGHIỆM THU

| Chỉ số nghiệm thu | Kết quả đạt được | Đánh giá Giảng viên |
| :--- | :--- | :--- |
| **Test Cases Nghiệm Thu Giảng Viên** | **150 / 150 PASSED (100%)** | **XUẤT SẮC (10/10)** |
| **Unit & Integration Tests Tự Động** | **466 / 466 PASSED (100%)** | **Tuyệt đối (0 lỗi, 0 skipped)** |
| **Lỗi Hệ Thống & Mã Nguồn (Bug List)** | **11 / 11 Đã khắc phục triệt để** | **100% RESOLVED** |
| **Mức độ bao phủ yêu cầu (Requirements)**| **49 / 49 Yêu cầu đạt chuẩn** | **100% PASS** |
| **Chất lượng mã nguồn (Code Quality)** | **0 Warning(s), 0 Error(s)** | **Build Release -warnaserror** |
| **Bảo mật Dependencies** | **0 lỗ hổng bảo mật** | **Vulnerable packages: 0** |

---

## 2. NĂM TRỤ CỘT KỸ THUẬT CỐT LÕI CỦA ĐỒ ÁN

### Trụ cột 1: Giao Thức Mạng & Lập Trình TCP Socket Thuần Túy
- **Wire Framing chuẩn**: Sử dụng 4-byte big-endian length prefix chống triệt để hiện tượng dính gói (packet sticking) và phân mảnh gói tin (packet fragmentation) trên đường truyền TCP.
- **Giới hạn khung an toàn**: Giới hạn `MaxPayloadBytes = 65,536` (64 KiB) bảo vệ máy chủ trước các cuộc tấn công Out-Of-Memory (OOM) do dữ liệu độc hại.
- **Cơ chế Heartbeat hai chiều**: Chu kỳ 10 giây gửi PING/PONG, timeout 30 giây tự động phát hiện kết nối chết một chiều (half-open / dead socket).

### Trụ cột 2: Kiến Trúc Authoritative Server & Quản Lý Phiên Đấu
- **Mô hình Server Thẩm Quyền (Authoritative Server)**: Mọi tính toán bàn cờ, đồng hồ thời gian, trạng thái thắng/thua/hòa đều do Server kiểm soát tập trung; Client chỉ là tầng hiển thị và gửi ý định nước đi (Move Intent).
- **Phân tách luồng xử lý**: Áp dụng cơ chế Khóa Tuần Tự Hóa (`SemaphoreSlim` / Mutex Gate) trên từng phòng đấu riêng biệt, cách ly hoàn toàn tài nguyên giữa các bàn cờ.
- **Cơ chế Reconnect 60 giây**: Khi rớt mạng, người chơi có cửa sổ 60 giây để kết nối lại thông qua Session Token ULID 256-bit bí mật; Server tự động chụp Snapshot nguyên tử và đồng bộ lại chính xác bàn cờ.

### Trụ cột 3: Engine Luật Cờ Tướng Độc Lập & Luật Quốc Tế Chặt Chẽ
- **Domain Engine độc lập**: Tách rời 100% khỏi Socket và Giao diện người dùng; hỗ trợ kiểm thử tự động với 236 bài test chuyên biệt.
- **Đầy đủ luật 7 loại quân cờ**: Cản chân Mã, mắt Tượng (không qua sông), ngòi Pháo, Xe, Sĩ/Tướng trong Cung và luật cấm hai Tướng nhìn thẳng mặt nhau.
- **Luật Cấm Chiếu Lặp / Đuổi Lặp (Repetition Law)**: Phát hiện và xử lý chu kỳ lặp nước đi theo Luật Cờ Tướng Quốc Tế, ép đổi nước hoặc xử hòa/thua chuẩn xác.

### Trụ cột 4: Dịch Vụ Thực Tế & Tích Hợp Hệ Thống Hoàn Chỉnh
- **Xác thực & Bảo mật tài khoản**: Đăng ký, đăng nhập với mã hóa PBKDF2/Argon2; tính năng Quên Mật Khẩu gửi mã OTP 6 số qua **SMTP Gmail thực tế** (hết hạn 10 phút, giới hạn gửi lại 60s).
- **Đấu cờ với Máy (Bot AI)**: Tích hợp Bot AI 3 cấp độ (Dễ, Trung bình, Khó) phản hồi tự động trong tích tắc.
- **Tính năng mở rộng**: Chat nhanh song ngữ trong phòng đấu UTF-8, chế độ Khán giả (Spectator) theo dõi trực tiếp ván cờ, và Bảng điều khiển Quản trị viên (ServerAdmin Dashboard).

### Trụ cột 5: Clean Architecture & Cơ Sở Dữ Liệu SQLite Bền Vững
- Tổ chức theo mô hình phân tầng chuẩn công nghiệp: `Shared Contracts`, `RuleEngine`, `Server`, `Client (WPF)`, `Persistence (SQLite v1.1)`, và `LoadTest`.
- Lưu trữ lịch sử toàn bộ các ván đấu, nước đi và vị trí bàn cờ phục vụ Replay tua lại ván cờ bất kỳ lúc nào.

---

## 3. DANH SÁCH 11 LỖI ĐÃ ĐƯỢC KHẮC PHỤC TRIỆT ĐỂ (11/11 RESOLVED)

1. **BUG-01 (Snapshot State Race Condition)**: Bổ sung `GetSnapshotState()` nguyên tử bọc trong `lock (_stateGate)` tại `GameRoom.cs` và `RoomMessages.cs`.
2. **BUG-02 (Head-of-Line Blocking Broadcast)**: Song song hóa việc gửi gói tin tới tất cả người chơi và khán giả bằng `Task.WhenAll` kèm timeout 3s tại `RoomEventBroadcaster.cs`.
3. **BUG-03 (Channel DropWrite Dropping Packets)**: Chuyển cấu hình `BoundedChannelFullMode` từ `DropWrite` sang `Wait` tại `ClientConnectionHandler.cs`.
4. **BUG-04 (Mất kết quả khi DB Lock)**: Triển khai retry loop 3 lần kèm backoff trong `TryPersistCompletion` tại `GameControlMessageHandler.cs`.
5. **BUG-05 (Lịch sử ván cờ > 64 KiB)**: Tối ưu lược bỏ position map dư thừa khi ván cờ vượt quá 35 nước tại `HistoryMessageHandler.cs`.
6. **BUG-06 (Client PING không xử lý PONG)**: Thêm nhánh `case "PONG"` trong `HandleMessageAsync`, cập nhật `LastPongUtc` và event `PongReceived` tại `GameClient.cs`.
7. **BUG-07 (Deduplication Request phạm vi socket)**: Gắn bộ đệm `TryRecordRequestId` theo `PlayerSession` và đồng bộ khi Reconnect tại `PlayerSession.cs` & `ClientConnectionHandler.cs`.
8. **BUG-08 (Graceful Shutdown)**: Bổ sung cơ chế `Task.WhenAll(drainTasks)` đợi xả hết các kết nối trước khi đóng socket tại `GameServerHost.cs`.
9. **BUG-09 (Classification DB không khớp mạng)**: Lưu đầy đủ facts JSON (`isCheck`, `isCheckmate`, `isCapture`, `classification`) vào SQLite tại `MoveCommittingService.cs`.
10. **BUG-10 (LoadTest PONG Latency)**: Đối chiếu chuẩn xác `CausationRequestId` trong `XiangqiOnline.LoadTest/Program.cs`.
11. **BUG-11 (ClockSync định kỳ)**: Tích hợp timer phát sóng `CLOCK_SYNC` định kỳ 1 giây tại `GameLifecycleMonitor.cs`.

---

## 4. TÀI LIỆU MINH CHỨNG & FILE ĐÁNH GIÁ ĐÍNH KÈM

- **File Excel Đánh Giá Giảng Viên**:  
  `Extra/test-evidence/UDM18_TestCases_Lecturer_Evaluation.xlsx`  
  *(Đã đồng bộ tại Desktop: `C:\Users\HUONG-DELL\Desktop\UDM18_TestCases_Lecturer_Evaluation.xlsx`)*
- **Log kiểm thử tự động 466 tests**:  
  `Extra/test-evidence/final-evaluation/test-run-466-passed.txt`
- **Schema cơ sở dữ liệu SQLite**:  
  `Extra/database/UDM18_Database_Schema_v1.1.sql`
