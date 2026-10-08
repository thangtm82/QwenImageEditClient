# Qwen Image Edit Client

Ứng dụng Windows Forms (.NET Framework 4.7.2) để thay background của ảnh bằng workflow Qwen-Image-2.1 chạy trên ComfyUI.

## Chuẩn bị

1. Cài Visual Studio 2022 với workload **.NET desktop development** và .NET Framework 4.7.2 Developer Pack.
2. Mở `QwenImageEditClient.sln`, **Restore NuGet Packages**, sau đó **Build** và chạy (F5).
3. Chạy ComfyUI Desktop và bật API/local server. Xác nhận URL của ComfyUI (mặc định `http://127.0.0.1:8188`; có thể khác trên máy bạn).
4. Cài các model mà workflow sử dụng: `qwen_image_2.1_int8_convrot.safetensors`, `qwen3vl_8b_int8_convrot.safetensors`, `qwen_image_2.1_vae_bf16.safetensors`. Trước tiên chạy thử `Workflows/background_replace.json` trực tiếp trong ComfyUI để kiểm tra node và model.

## Sử dụng

1. Nhập ComfyUI URL và chọn **ảnh đầu vào**.
2. Chọn thư mục lưu ảnh, nhập mô tả **background mới** (có thể viết tiếng Anh).
3. Giữ `Resolution = 1024`, `Steps = 16` để thử tốc độ; điều chỉnh nếu cần.
4. Bấm **Thay background**. Khi xong, ảnh hiển thị bên phải và PNG được lưu vào thư mục đã chọn.

Ứng dụng gửi workflow **API format**, upload ảnh bằng `/upload/image`, đưa job vào `/prompt`, kiểm tra `/history/{prompt_id}` và tải node output `461` bằng `/view`. Ảnh đầu vào được đặt tên riêng trên ComfyUI cho từng lượt chạy. Nút **Hủy chờ** dừng ứng dụng chờ kết quả, không dừng job đã gửi trên ComfyUI. Nếu quá 30 phút, ứng dụng báo `prompt_id` để kiểm tra trong ComfyUI.

File workflow trong repo được tạo từ workflow gốc do người dùng cung cấp, với `resolution = 1024`, `steps = 16`, `cache dtype = int8`, prompt thay nền. Template giữ nguyên model và graph gốc. Câu lệnh prompt chỉ hướng dẫn model giữ biển neon; ảnh xuất ra vẫn cần kiểm tra chữ và chi tiết trước khi dùng thực tế.

Nếu ComfyUI trả HTTP 400, xem thông báo lỗi trong ứng dụng và kiểm tra tên model/node trong ComfyUI Desktop. ComfyUI Desktop không được mở API ra Internet nếu chưa thiết lập bảo vệ phù hợp.
