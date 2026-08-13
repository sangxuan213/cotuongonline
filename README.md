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
