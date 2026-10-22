# Gửi File LAN

![Logo Gửi File LAN](Assets/logo.png)

Ứng dụng Windows 64-bit để gửi file và thư mục trực tiếp giữa các máy cùng mạng nội bộ. Dữ liệu truyền qua TLS; máy nhận phải chấp thuận trước khi ứng dụng ghi file. Không dùng máy chủ đám mây.

## Sử dụng

1. Cài và mở ứng dụng trên cả hai máy tính đang dùng cùng mạng Wi-Fi/mạng nội bộ.
2. Trên máy gửi, chọn máy nhận, chọn một hoặc nhiều file hoặc cả thư mục, rồi nhấn **Gửi an toàn**.
3. Trên máy nhận, xem tên máy gửi, danh sách file và thư mục đích rồi chấp thuận hoặc từ chối.
4. Đổi thư mục nhận ở khung **Nơi lưu file nhận**. File được lưu trong thư mục con `Gửi File LAN\<tên máy gửi>`.
5. Nhấn nút ngôn ngữ ở góc trên bên phải để đổi giữa tiếng Việt và tiếng Anh.

Tên file trùng được đổi tự động để file hiện có không bị ghi đè. File đang nhận được ghi vào file tạm và chỉ xuất hiện dưới tên hoàn chỉnh sau khi nhận đủ dữ liệu.

## Bản cài đặt và dung lượng

Chạy `powershell -ExecutionPolicy Bypass -File .\build.ps1` để tạo bản phát hành.

- `artifacts\GuiFileLAN-Setup-win-x64.exe`: trình cài đặt tự chạy, không cần cài .NET Runtime riêng; phù hợp cài mới hoặc dùng ngoại tuyến.
- `artifacts\GuiFileLAN-win-x64.zip`: bản ZIP tự chạy, không cần cài .NET Runtime.
- `artifacts\GuiFileLAN-Compact-win-x64.zip`: bản gọn, cần **.NET 10 Desktop Runtime x64** trên máy.

Bản gọn tránh đóng gói lại .NET Desktop Runtime trên mỗi máy; bản tự chạy có dung lượng lớn hơn nhưng cài được ngay. Cả hai đều là bản Windows 64-bit.

## Chạy từ mã nguồn và đóng gói

- Cài .NET 10 SDK trên Windows.
- Chạy `dotnet run --project .\GửiFileLAN.csproj`.
- Chạy `powershell -ExecutionPolicy Bypass -File .\build.ps1` để tạo các gói phát hành. Cài Inno Setup 6 để tạo thêm trình cài đặt.
- Sau khi cập nhật, cài cùng phiên bản trên cả máy gửi và máy nhận.

## Windows Firewall và kết nối

Khi Windows hỏi quyền truy cập mạng, cho phép ứng dụng trên **mạng riêng tư**. Nếu đã từ chối trước đó, mở Windows Security → Firewall & network protection → Allow an app through firewall, rồi cho phép `GuiFileLAN.exe` trên mạng riêng tư.

Ứng dụng dùng UDP cổng 45831 để tìm thiết bị và TCP cổng 45832 để truyền file. Nếu hai máy không thấy nhau, kiểm tra chúng ở cùng mạng Wi-Fi, mạng đang được đặt là Private, và router không bật chế độ cô lập thiết bị/client isolation. Mạng khách hoặc mạng công cộng có thể chặn kết nối giữa các máy.

**Nhà phát triển:** PhuDong · 0766771509

## Đóng góp

Hướng dẫn báo lỗi, đề xuất tính năng và gửi pull request có trong [CONTRIBUTING.md](CONTRIBUTING.md). Mẫu issue có sẵn trong GitHub để thu thập phiên bản Windows và các bước tái hiện.

## Build tự động

GitHub Actions build các gói Windows x64 khi có push lên `main`, pull request hoặc chạy thủ công. Gói build được đính kèm trong mục **Actions → Artifacts**.

## Bảo mật

Không đăng công khai thông tin có thể khai thác hoặc dữ liệu cá nhân. Xem [SECURITY.md](SECURITY.md) để biết cách báo cáo vấn đề bảo mật.

## Giấy phép

Mã nguồn được cấp phép theo MIT. Bạn có thể sử dụng, chỉnh sửa, phân phối và dùng thương mại; cần giữ thông báo bản quyền và giấy phép trong các bản sao hoặc phần đáng kể của phần mềm. Xem nội dung đầy đủ trong [LICENSE](LICENSE).
