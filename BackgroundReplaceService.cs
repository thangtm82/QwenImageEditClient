using Newtonsoft.Json.Linq;
using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace QwenImageEditClient
{
    internal sealed class ReplaceOptions
    {
        public string ImagePath { get; set; }
        public string OutputFolder { get; set; }
        public string WorkflowPath { get; set; }
        public string Background { get; set; }
        public int Resolution { get; set; }
        public int Steps { get; set; }
    }

    internal sealed class ReplaceResult
    {
        public string PromptId { get; set; }
        public string SavedPath { get; set; }
    }

    internal sealed class BackgroundReplaceService
    {
        private readonly ComfyUiClient client;
        public BackgroundReplaceService(ComfyUiClient client) { this.client = client; }

        public async Task<ReplaceResult> RunAsync(ReplaceOptions options, IProgress<string> progress, CancellationToken cancellationToken)
        {
            if (!File.Exists(options.ImagePath)) throw new FileNotFoundException("Không tìm thấy ảnh input.", options.ImagePath);
            if (!File.Exists(options.WorkflowPath)) throw new FileNotFoundException("Không tìm thấy workflow API JSON.", options.WorkflowPath);
            if (string.IsNullOrWhiteSpace(options.Background)) throw new ArgumentException("Hãy nhập mô tả background mới.");
            if (options.Steps < 1 || options.Resolution < 1) throw new ArgumentException("Steps và resolution phải lớn hơn 0.");

            progress.Report("Đang kết nối ComfyUI...");
            await client.CheckConnectionAsync(cancellationToken);

            progress.Report("Đang upload ảnh...");
            string uploadedName = await client.UploadImageAsync(options.ImagePath, cancellationToken);
            var workflow = JObject.Parse(File.ReadAllText(options.WorkflowPath));

            // Các ID này thuộc file Workflows/background_replace.json (API format).
            SetInput(workflow, "470", "image", uploadedName);
            SetInput(workflow, "459:474", "prompt", BuildPrompt(options.Background));
            SetInput(workflow, "459:474", "resolution", options.Resolution);
            SetInput(workflow, "459:458", "steps", options.Steps);
            SetInput(workflow, "459:458", "seed", NewSeed());
            SetInput(workflow, "459:469", "device", "auto");
            SetInput(workflow, "459:469", "dtype", "int8");
            SetInput(workflow, "461", "filename_prefix", "BackgroundReplace");

            progress.Report("Đang gửi workflow...");
            string promptId = await client.QueueAsync(workflow, cancellationToken);
            progress.Report("Đang xử lý trên ComfyUI. Prompt ID: " + promptId);

            // Không gọi /interrupt khi hủy: endpoint đó dừng cả lượt chạy của người khác.
            var deadline = DateTime.UtcNow.AddMinutes(30);
            while (DateTime.UtcNow < deadline)
            {
                cancellationToken.ThrowIfCancellationRequested();
                JObject history = await client.GetHistoryAsync(promptId, cancellationToken);
                var job = history[promptId] as JObject;
                if (job != null)
                {
                    string status = (string)job["status"]?["status_str"];
                    bool completed = (bool?)job["status"]?["completed"] ?? false;
                    if (status == "error" || status == "execution_error")
                        throw new InvalidOperationException("Workflow lỗi: " + (job["status"]?["messages"]?.ToString() ?? "Không có thông tin chi tiết."));

                    var images = job["outputs"]?["461"]?["images"] as JArray;
                    if (images != null && images.Count > 0)
                    {
                        string filename = (string)images[0]["filename"];
                        string subfolder = (string)images[0]["subfolder"] ?? "";
                        string type = (string)images[0]["type"] ?? "output";
                        if (string.IsNullOrWhiteSpace(filename)) throw new InvalidOperationException("Output không có filename.");

                        progress.Report("Đang tải kết quả...");
                        byte[] bytes = await client.DownloadAsync(filename, subfolder, type, cancellationToken);
                        Directory.CreateDirectory(options.OutputFolder);
                        string name = Path.GetFileNameWithoutExtension(options.ImagePath) + "_background_"
                            + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + "_" + promptId.Substring(0, Math.Min(8, promptId.Length)) + ".png";
                        string path = Path.Combine(options.OutputFolder, name);
                        File.WriteAllBytes(path, bytes);
                        return new ReplaceResult { PromptId = promptId, SavedPath = path };
                    }
                    if (completed || status == "success")
                        throw new InvalidOperationException("Workflow hoàn tất nhưng node 461 không tạo ảnh. Kiểm tra output trong ComfyUI.");
                }
                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
            }
            throw new TimeoutException("Quá 30 phút chưa nhận được kết quả. Prompt ID: " + promptId);
        }

        private static void SetInput(JObject workflow, string node, string input, object value)
        {
            var inputs = workflow[node]?["inputs"] as JObject;
            if (inputs == null || inputs.Property(input) == null)
                throw new InvalidOperationException("Workflow thiếu node/input " + node + "/" + input + ". Hãy dùng workflow API đi kèm.");
            inputs[input] = JToken.FromObject(value);
        }

        private static string BuildPrompt(string background)
        {
            return "Keep the original neon sign exactly unchanged: preserve every letter, typography, layout, colors, shape, proportions and glow. "
                + "Replace only the existing background with " + background.Trim() + ". "
                + "Match the scene lighting and perspective naturally. Keep the neon sign clearly visible, uncropped, and in the foreground. "
                + "Do not redraw, deform, change or remove any part of the sign.";
        }

        private static long NewSeed()
        {
            var bytes = new byte[8];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(bytes);
            return (long)(BitConverter.ToUInt64(bytes, 0) % 1000000000000000UL);
        }
    }
}
