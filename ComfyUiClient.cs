using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace QwenImageEditClient
{
    internal sealed class ComfyUiClient : IDisposable
    {
        private readonly HttpClient http = new HttpClient { Timeout = TimeSpan.FromSeconds(90) };
        private readonly string baseUrl;

        public ComfyUiClient(string baseUrl)
        {
            Uri uri;
            if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                throw new ArgumentException("ComfyUI URL phải bắt đầu bằng http:// hoặc https://.");
            this.baseUrl = baseUrl.TrimEnd('/');
        }

        public async Task CheckConnectionAsync(CancellationToken cancellationToken)
        {
            using (var response = await http.GetAsync(baseUrl + "/system_stats", cancellationToken))
                await EnsureSuccessAsync(response, "Kết nối ComfyUI");
        }

        public async Task<string> UploadImageAsync(string filePath, CancellationToken cancellationToken)
        {
            using (var stream = File.OpenRead(filePath))
            using (var form = new MultipartFormDataContent())
            {
                // Tên riêng cho mỗi lần upload tránh ghi đè ảnh của các lượt chạy khác.
                string uploadName = Guid.NewGuid().ToString("N") + Path.GetExtension(filePath).ToLowerInvariant();
                form.Add(new StreamContent(stream), "image", uploadName);
                form.Add(new StringContent("input"), "type");
                form.Add(new StringContent("false"), "overwrite");

                using (var response = await http.PostAsync(baseUrl + "/upload/image", form, cancellationToken))
                {
                    string body = await EnsureSuccessAsync(response, "Upload ảnh");
                    var json = JObject.Parse(body);
                    string name = (string)json["name"];
                    if (string.IsNullOrWhiteSpace(name)) throw new InvalidOperationException("ComfyUI không trả về tên ảnh đã upload.");
                    string subfolder = (string)json["subfolder"];
                    return string.IsNullOrEmpty(subfolder) ? name : subfolder.TrimEnd('/', '\\') + "/" + name;
                }
            }
        }

        public async Task<string> QueueAsync(JObject workflow, CancellationToken cancellationToken)
        {
            var body = new JObject
            {
                ["prompt"] = workflow,
                ["client_id"] = Guid.NewGuid().ToString("N")
            };
            using (var content = new StringContent(body.ToString(Formatting.None), Encoding.UTF8, "application/json"))
            using (var response = await http.PostAsync(baseUrl + "/prompt", content, cancellationToken))
            {
                string result = await EnsureSuccessAsync(response, "Đưa workflow vào hàng đợi");
                var json = JObject.Parse(result);
                string promptId = (string)json["prompt_id"];
                if (string.IsNullOrWhiteSpace(promptId))
                    throw new InvalidOperationException("ComfyUI không trả về prompt_id: " + result);
                return promptId;
            }
        }

        public async Task<JObject> GetHistoryAsync(string promptId, CancellationToken cancellationToken)
        {
            using (var response = await http.GetAsync(baseUrl + "/history/" + Uri.EscapeDataString(promptId), cancellationToken))
                return JObject.Parse(await EnsureSuccessAsync(response, "Đọc tiến trình"));
        }

        public async Task<byte[]> DownloadAsync(string filename, string subfolder, string type, CancellationToken cancellationToken)
        {
            string url = baseUrl + "/view?filename=" + Uri.EscapeDataString(filename)
                + "&subfolder=" + Uri.EscapeDataString(subfolder ?? "")
                + "&type=" + Uri.EscapeDataString(type ?? "output");
            using (var response = await http.GetAsync(url, cancellationToken))
            {
                if (!response.IsSuccessStatusCode)
                    await EnsureSuccessAsync(response, "Tải ảnh kết quả");
                return await response.Content.ReadAsByteArrayAsync();
            }
        }

        private static async Task<string> EnsureSuccessAsync(HttpResponseMessage response, string operation)
        {
            string body = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException(operation + " thất bại (HTTP " + (int)response.StatusCode + "): " + body);
            return body;
        }

        public void Dispose() { http.Dispose(); }
    }
}
