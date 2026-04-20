# PRODUCT REQUIREMENTS DOCUMENT (PRD)

## Dự án
- **Tên dự án:** AmThucVinhKhanh - Ứng dụng thuyết minh và khám phá Phố ẩm thực Vĩnh Khánh
- **Phiên bản tài liệu:** v1.1 (based on `AmThucVinhKhanh_github_latest`)
- **Ngôn ngữ:** Tiếng Việt
- **Ngày cập nhật:** 16/04/2026

## 1) Bối cảnh và bài toán
Khu phố ẩm thực Vĩnh Khánh có mật độ quán cao, nhiều khách mới nhưng thiếu trải nghiệm số hóa đồng nhất: khó tìm quán phù hợp, nội dung thuyết minh không nhất quán giữa điểm, và thiếu dữ liệu phân tích hành vi theo thời gian thực cho bên vận hành.

Sản phẩm AmThucVinhKhanh giải quyết bằng mô hình app + CMS:
- App hỗ trợ khám phá POI trên bản đồ, quét QR và nghe thuyết minh.
- CMS quản trị POI, nội dung, tài khoản và báo cáo.
- Dữ liệu đồng bộ qua API + SQLite để đảm bảo demo ổn định.

## 2) Mục tiêu sản phẩm
- Số hóa trải nghiệm khám phá ẩm thực theo địa điểm thực tế.
- Tăng khả năng tiếp cận thông tin quán theo vị trí và theo ngôn ngữ.
- Chuẩn hóa nội dung mô tả và nội dung thuyết minh từng POI.
- Vận hành cơ chế khóa/mở nội dung nâng cao theo POI qua QR thanh toán demo.
- Cung cấp báo cáo lượt phát, lộ trình, heatmap và doanh thu demo.

## 3) Phạm vi
### 3.1 In-scope
- Ứng dụng MAUI cho khách:
  - Đăng ký/đăng nhập/đăng xuất.
  - Xem map, GPS thời gian thực, marker POI.
  - Tìm kiếm và xem thông tin địa điểm.
  - Quét QR và mở nội dung theo POI.
  - Nghe thuyết minh đa ngôn ngữ.
  - Theo dõi lịch sử hành trình, heatmap.
- CMS Web:
  - Đăng nhập vai trò admin/chủ quán.
  - CRUD POI và quản trị nội dung.
  - Quản lý tài khoản khách/chủ quán.
  - Báo cáo doanh thu demo, lịch sử lượt phát, route/heatmap.

### 3.2 Out-of-scope
- Cổng thanh toán production.
- Phân hệ moderation đa cấp hoàn chỉnh.
- Hệ thống thông báo đẩy production.

## 4) Tác nhân hệ thống (Actors)
- **Khách hàng:** khám phá và nghe thuyết minh địa điểm.
- **Chủ quán:** quản lý nội dung POI thuộc quán.
- **Quản trị viên:** quản lý toàn hệ thống, theo dõi dữ liệu vận hành.

## 5) Yêu cầu chức năng
### FR-01 Xác thực & phiên làm việc
- Người dùng có thể đăng ký/đăng nhập/đăng xuất.
- Hệ thống lưu phiên cục bộ, hỗ trợ xóa session/clear local data.

### FR-02 Quản lý POI
- CMS cho phép CRUD POI.
- Trường dữ liệu gồm tên, mô tả, tọa độ, ảnh, map URL, specialty, thuyết minh đa ngôn ngữ.
- App lấy POI từ API và có khả năng fallback local.

### FR-03 Bản đồ & định vị
- Hiển thị vị trí GPS, POI marker, popup thông tin.
- Chọn POI và di chuyển demo tới POI.
- Mở chi tiết POI trực tiếp từ map.

### FR-04 Quét QR & thanh toán demo
- Quét QR để mở luồng nội dung/thanh toán theo POI.
- Ghi nhận trạng thái thanh toán mở khóa theo user/device.
- Doanh thu demo hiển thị được định danh người trả phù hợp.

### FR-05 Thuyết minh đa ngôn ngữ
- Hỗ trợ 4 ngôn ngữ (Việt/Anh/Trung/Nhật).
- Parser nhận được cả payload camelCase/snake_case.
- Không để mô tả bị dính vào 4 trường thuyết minh.

### FR-06 Báo cáo & phân tích
- Lưu lịch sử lượt phát.
- Xem lịch sử hành trình/route.
- Xem heatmap điểm nóng.
- Xem doanh thu demo theo POI và định danh liên quan.

## 6) Yêu cầu phi chức năng
### NFR-01 Hiệu năng
- Tương tác map và chọn POI phản hồi nhanh, không đơ UI.
- Tải dữ liệu POI ổn định trong điều kiện LAN.

### NFR-02 Ổn định
- API lỗi/mất kết nối: app không crash, có fallback dữ liệu local.
- CMS chạy độc lập, app có thể tiếp tục trải nghiệm mức cơ bản.

### NFR-03 Bảo trì
- Kiến trúc service tách lớp rõ, dễ mở rộng.
- Có thể thêm POI mới không làm sai dữ liệu audio/mô tả.

### NFR-04 Bảo mật cơ bản
- Quản lý phân quyền actor.
- Tránh lưu dữ liệu nhạy cảm không cần thiết trên client.

## 7) Kiến trúc tổng quan
- **Mobile:** .NET MAUI + service layer + local repository + webview map.
- **CMS:** ASP.NET Core Razor Pages + repository/service.
- **Database:** SQLite (`VinhKhanh.db`) cho dữ liệu vận hành demo.
- **Kết nối:** API URL cấu hình + fallback URL khi môi trường thay đổi.

## 8) Luồng chính
### 8.1 Khách khám phá
Đăng nhập -> mở map -> chọn POI -> xem thông tin -> nghe thuyết minh.

### 8.2 Luồng mở khóa premium theo POI
Chọn POI premium -> quét QR -> trang Pay -> ghi nhận giao dịch demo -> mở nội dung.

### 8.3 Luồng quản trị
Admin/Chủ quán đăng nhập CMS -> cập nhật nội dung -> theo dõi báo cáo.

## 9) KPI đề xuất
- Tỷ lệ tải map + POI thành công: >= 95%.
- Tỷ lệ phát thuyết minh thành công: >= 95%.
- Tỷ lệ ghi nhận giao dịch demo đúng định danh: >= 98%.
- Tỷ lệ đồng bộ nội dung POI mới: >= 98%.

## 10) Rủi ro và giảm thiểu
- **Mạng/LAN thay đổi:** fallback nhiều URL và cấu hình linh hoạt.
- **Sai mapping API field:** ưu tiên parser chọn bản dữ liệu đầy đủ.
- **Sai lệch tài liệu so với code:** rà định kỳ sơ đồ usecase/sequence/activity/class.
- **Lỗi build do process lock:** chuẩn hóa quy trình stop service trước build.

## 11) UAT checklist
- Đăng ký/đăng nhập/đăng xuất đầy đủ vai trò.
- Thêm POI mới và xác minh mô tả + 4 text thuyết minh tách biệt.
- Quét QR và kiểm tra mở khóa + doanh thu demo.
- Test map online/offline API.
- Test lịch sử hành trình, heatmap, revenue.

## 12) Sơ đồ đính kèm
- Chèn ảnh activity từ thư mục `Compressed`.
- Đính kèm file `.drawio` từ `Compressed` và `usecase (1).drawio`.
- `usecase (1).drawio` đã chỉnh nhãn quan hệ nghiệp vụ theo hướng chuẩn hóa (`include/extend`).

## 13) Backlog ưu tiên
- [P1] Fallback UI thân thiện khi map/API không khả dụng (tránh black screen).
- [P1] Hoàn thiện mapping chỉnh sửa 1-1 toàn bộ sơ đồ draw.io theo code hiện hành.
- [P2] Chuẩn hóa checklist release-demo trước buổi bảo vệ.
