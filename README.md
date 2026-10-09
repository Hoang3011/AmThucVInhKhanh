# Ẩm Thực Vĩnh Khánh – Ứng dụng thuyết minh phố ẩm thực

Đồ án C# xây dựng hệ thống **app du lịch + web quản trị (CMS)** cho phố ẩm thực Vĩnh Khánh, Quận 4, TP.HCM.
Du khách mở app, đi đến gần một quán/đặc sản (POI) là app tự phát thuyết minh; chủ quán và quản trị viên quản lý nội dung và xem báo cáo trên web.

## Tính năng chính

**App khách hàng (.NET MAUI)**
- Đăng ký / đăng nhập / đăng xuất, lưu phiên cục bộ.
- Bản đồ, GPS thời gian thực, marker các quán (POI), popup thông tin.
- Geofence: vào vùng quán thì tự phát thuyết minh (có hàng đợi, tự hủy khi đổi ngữ cảnh).
- Thuyết minh đa ngôn ngữ: Việt / Anh / Trung / Nhật, dễ mở rộng thêm quán và ngôn ngữ.
- Quét / tạo mã QR để mở nội dung theo quán; mở khóa nội dung nâng cao bằng thanh toán demo.
- Lịch sử hành trình, lịch sử nghe, heatmap điểm nóng.
- Dữ liệu lấy từ API của CMS, có SQLite cục bộ làm phương án dự phòng.

**Web quản trị (ASP.NET Core Razor Pages)**
- Đăng nhập theo vai trò quản trị viên / chủ quán.
- CRUD quán (POI), nội dung thuyết minh, ảnh, tọa độ.
- Quản lý tài khoản khách hàng và chủ quán.
- Báo cáo: lượt nghe, lịch sử phát, doanh thu demo, thiết bị đang online, lộ trình và heatmap khách hàng.
- Tạo QR tải app (APK) và QR nghe thuyết minh; REST API cho app (`/api/places`, `/api/plays/log`, `/api/devices/heartbeat`, …).

## Cấu trúc thư mục

```
.
├── backend/                    # Cơ sở dữ liệu SQLite
│   ├── App_Data/VinhKhanh.db   # DB đang dùng (CMS đọc/ghi, app MAUI đóng gói kèm)
│   └── VinhKhanh.db            # DB mẫu ban đầu, dùng để khởi tạo khi chưa có App_Data
├── frontend/
│   ├── TourGuideApp2/          # App khách hàng trong tour du lịch (.NET MAUI)
│   └── TourGuideCMS/           # Web admin quản lý (ASP.NET Core Razor Pages + REST API)
├── TourGuideApp2.slnx          # Solution gồm cả app và CMS
├── PRD_*.docx / PRD_*.md       # Tài liệu yêu cầu sản phẩm
├── BaoCaoTienDo.md             # Báo cáo tiến độ
└── *.drawio                    # Sơ đồ use case / luồng chức năng
```

## Công nghệ

- .NET 10, .NET MAUI (Android, iOS, MacCatalyst, Windows)
- ASP.NET Core Razor Pages (CMS)
- SQLite (`Microsoft.Data.Sqlite`, `sqlite-net-pcl`)
- Thư viện: CommunityToolkit.Mvvm / Maui, Plugin.Maui.Audio, ZXing.Net.Maui, QRCoder, Syncfusion.Maui.Toolkit

## Chạy dự án

Cần cài .NET 10 SDK và workload MAUI (`dotnet workload install maui`).

**1. Chạy web admin (CMS)**
```bash
dotnet run --project frontend/TourGuideCMS
```
CMS tự dùng DB trong `backend/App_Data`. Có thể đổi vị trí bằng cấu hình `Database:Directory`.
Đổi `AdminPassword` trong `frontend/TourGuideCMS/appsettings.json` trước khi triển khai.

**2. Chạy app khách hàng**

Sửa địa chỉ CMS (`DefaultPoiApiUrl`, `DefaultPublicCmsBaseUrl`) trong `frontend/TourGuideApp2/AppConfig.cs` cho đúng máy/tunnel của bạn, rồi:
```bash
dotnet build frontend/TourGuideApp2 -f net10.0-android
```
App tự đóng gói `backend/App_Data/VinhKhanh.db` (hoặc `backend/VinhKhanh.db` nếu chưa có) làm dữ liệu cục bộ.

## Sinh viên thực hiện

| MSSV | Họ tên |
|------|--------|
| 3123411124 | Phạm Minh Huy |
| 3123411106 | Nguyễn Xuân Hoàng |
