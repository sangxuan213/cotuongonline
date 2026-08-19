# UDM_18 - Game Cờ Tướng Trực Tuyến

Ứng dụng Desktop Cờ Tướng trực tuyến theo mô hình Client-Server.

## Công nghệ

- C#
- .NET 10
- WPF
- TCP
- JSON với 4-byte length prefix
- SQLite
- xUnit

## Cấu trúc repository

- `Code/src/XiangqiOnline.Shared`: Shared contracts và DTO
- `Code/src/XiangqiOnline.RuleEngine`: Luật và phán quyết Cờ Tướng
- `Code/src/XiangqiOnline.Server`: Authoritative Server
- `Code/src/XiangqiOnline.Client`: WPF Client
- `Code/tests`: Unit, protocol, server và integration tests
- `Code/tools/XiangqiOnline.LoadTest`: Công cụ kiểm thử tải
- `DOCX`: Báo cáo
- `PPTX`: Slide thuyết trình
- `Extra`: Evidence, log, database và video

## Quy trình nhánh

- `main`: Bản ổn định
- `develop`: Nhánh tích hợp
- `feature/*`: Nhánh chức năng
- `chore/*`: Nhánh công việc kỹ thuật

Không push chức năng trực tiếp vào `main`.

## Kiểm tra Client Phase 1

```powershell
dotnet build Code/src/XiangqiOnline.Client/XiangqiOnline.Client.csproj -c Release -warnaserror
dotnet run --project Code/tests/XiangqiOnline.Client.SmokeTests/XiangqiOnline.Client.SmokeTests.csproj -c Release
```

Client dùng Shared Contracts tại `Code/src/XiangqiOnline.Shared/Contracts` và chỉ cập nhật bàn cờ sau event authoritative từ Server.

## Chạy bản hoàn chỉnh Phase 2-4 (.NET 10)

```powershell
$env:MSBuildEnableWorkloadResolver='false'
dotnet build Code/XiangqiOnline.slnx -c Release
dotnet run --project Code/src/XiangqiOnline.Server/XiangqiOnline.Server.csproj -c Release
dotnet run --project Code/src/XiangqiOnline.Client/XiangqiOnline.Client.csproj -c Release
```

Mặc định Server lắng nghe tại `0.0.0.0:5000`; Client mặc định kết nối `127.0.0.1:5000`.

## Đăng nhập, đăng ký và gửi mã quên mật khẩu

Trang đầu hỗ trợ tài khoản email, đăng ký tự động vào sảnh, đặt lại mật khẩu bằng mã 6 số và chế độ chơi nhanh bằng tên khách. Mã hết hạn sau 10 phút, chỉ dùng một lần; yêu cầu gửi lại bị giới hạn 60 giây.

Trước khi chạy Server, cấu hình SMTP bằng biến môi trường (không ghi mật khẩu email vào `appsettings.json`):

```powershell
$env:XIANGQI_SMTP_HOST='smtp.gmail.com'
$env:XIANGQI_SMTP_PORT='587'
$env:XIANGQI_SMTP_USER='email-cua-ban@gmail.com'
$env:XIANGQI_SMTP_PASSWORD='mat-khau-ung-dung-16-ky-tu'
$env:XIANGQI_SMTP_FROM='email-cua-ban@gmail.com'
$env:XIANGQI_RESET_PEPPER='chuoi-bi-mat-rieng-dai-it-nhat-16-ky-tu'
./start.bat
```

Với Gmail phải dùng **Mật khẩu ứng dụng**, không dùng mật khẩu đăng nhập Gmail. Khi đưa lên hosting, khai báo các biến này trong phần Environment/Secrets của hosting.

Các luồng đã nối Server thật:

- Nhiều phòng độc lập, revision tuần tự và writer queue theo connection.
- Đồng hồ monotonic 60+30, 10+5, 5+3, 3+2; timeout do Server phán quyết.
- Đầu hàng, đề nghị/chấp nhận hòa, checkmate/no-legal-move và kết quả exactly-once.
- Reconnect bằng token 256-bit trong cửa sổ 60 giây; snapshot/resync atomic.
- Danh sách trận hoạt động, spectator vào giữa trận, read-only và nhận realtime event.
- Lịch sử match/move/position, replay theo revision và lưu end reason.
- Classification CHECK/CHASE/CAPTURE/IDLE, cảnh báo must-vary và phán quyết chu kỳ lặp.
- Protocol version validation, request deduplication, rate limit, graceful shutdown và token redaction.

## Kiểm thử và tải

```powershell
$env:MSBuildEnableWorkloadResolver='false'
dotnet test Code/XiangqiOnline.slnx -c Release

# Load A: 10 kết nối thật
dotnet run --project Code/tools/XiangqiOnline.LoadTest/XiangqiOnline.LoadTest.csproj -c Release -- --host 127.0.0.1 --port 5000 --clients 10 --games 5 --spectators 5 --duration 60

# Load B: 40 kết nối thật
dotnet run --project Code/tools/XiangqiOnline.LoadTest/XiangqiOnline.LoadTest.csproj -c Release -- --host 127.0.0.1 --port 5000 --clients 40 --games 15 --spectators 10 --duration 120
```

Load tool xuất JSON và CSV gồm P50/P95/P99, error count và cấu hình chạy vào `Extra/test-evidence/load`.
