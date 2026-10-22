# Đóng góp

Cảm ơn bạn đã quan tâm đóng góp cho Gửi File LAN.

## Báo lỗi hoặc đề xuất

- Tìm issue hiện có trước khi mở issue mới.
- Dùng mẫu issue phù hợp và ghi phiên bản ứng dụng, phiên bản Windows, các bước tái hiện cùng kết quả mong đợi.
- Không đính kèm tài liệu riêng tư, thông tin đăng nhập hoặc dữ liệu nhạy cảm.

## Thay đổi mã nguồn

1. Tạo nhánh riêng cho thay đổi.
2. Giữ giao diện tiếng Việt và tiếng Anh đồng bộ khi thêm hoặc sửa nội dung hiển thị.
3. Chạy `dotnet build -c Release` trên Windows với .NET 10 SDK.
4. Với thay đổi gửi/nhận, kiểm tra thủ công trên hai máy Windows cùng mạng nội bộ; xác nhận máy nhận phải chấp thuận trước khi file được ghi.
5. Mở pull request, mô tả thay đổi, cách kiểm tra và ảnh chụp nếu có thay đổi giao diện.

## Phạm vi dự án

Ứng dụng truyền trực tiếp trong mạng nội bộ. Không thêm dịch vụ đám mây hoặc phụ thuộc máy chủ bên ngoài nếu chưa được thảo luận trong issue.
