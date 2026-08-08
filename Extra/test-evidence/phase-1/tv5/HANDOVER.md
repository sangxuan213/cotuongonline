# TV5 Phase 1 handover

## Đã hoàn thành

- WPF MVVM shell cho Connection, Lobby và GameRoom.
- TCP JSON framing với header 4 byte big-endian.
- HELLO, login, player list, challenge và room flow.
- Bàn cờ 9x10, 32 quân, canonical coordinate và xoay bàn.
- MOVE_REQUEST chỉ thay đổi bàn sau MOVE_COMMITTED.
- Pending guard, MOVE_REJECTED, capture, current turn và revision handling.
- RESYNC_REQUEST khi thiếu revision hoặc không tìm thấy quân.
- Shared Contracts dùng chung cho DTO và enum.
- UTF-8 cho toàn bộ nội dung giao diện.
- 15 smoke tests và clean build .NET 10.
- Demo mode vượt qua kiểm tra khởi động ứng dụng.

## Gate cần phối hợp bên ngoài

- Chưa ghi nhận demo hai Client thật vì repository hiện chưa có Server Phase 1 chạy hoàn chỉnh để thực hiện kịch bản lobby, challenge và move end-to-end.
- Cần TV2 review và phê duyệt theo cặp reviewer đã khóa.
- PR tiếp theo phải đi vào `develop`; PR #3 trước đó đã được merge vào `main`.

Không dùng tài liệu này để tự xác nhận Phase 1 PASS khi các gate phối hợp bên ngoài chưa hoàn tất.
