using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;

namespace CICAPOS.Plugins.Ai;

public interface INovaOcrClient
{
    string ExtractText(byte[] fileBytes, string? fileName);
}

/// <summary>
/// D.Nova OCR: upload PDF/image → poll status → return plain text.
/// Docs: https://nova.deloitte.com.cn → OCR接口文档
/// POST {base}/callOcrFileParse  (multipart file + fileInfo.apiKey)
/// </summary>
public sealed class NovaOcrClient : INovaOcrClient
{
    public const string DefaultBaseUrl = "https://nova.deloitte.com.cn/nova-cloud/ocr/ocr/v1";

    readonly AiProviderConfig _config;
    readonly Func<HttpRequestMessage, byte[]> _send;

    public NovaOcrClient(AiProviderConfig config, Func<HttpRequestMessage, byte[]>? send = null)
    {
        _config = config;
        _send = send ?? DefaultSend;
    }

    public string ExtractText(byte[] fileBytes, string? fileName)
    {
        if (!_config.HasOcr)
            throw new InvalidOperationException("D.Nova OCR is not configured (NOVA_OCR_API_KEY).");
        if (fileBytes == null || fileBytes.Length == 0)
            return "";

        var baseUrl = string.IsNullOrWhiteSpace(_config.OcrBaseUrl) ? DefaultBaseUrl : _config.OcrBaseUrl!.TrimEnd('/');
        var uploadUrl = baseUrl + "/callOcrFileParse";
        var boundary = "----cicapos" + Guid.NewGuid().ToString("N");
        var fileInfoJson = "{\"apiKey\":" + Quote(_config.OcrApiKey!) + ",\"callbackFlag\":\"0\"}";
        var body = BuildMultipart(boundary, fileName ?? "upload.pdf", fileBytes, fileInfoJson);

        using (var req = new HttpRequestMessage(HttpMethod.Post, uploadUrl))
        {
            req.Content = new ByteArrayContent(body);
            req.Content.Headers.ContentType = MediaTypeHeaderValue.Parse("multipart/form-data; boundary=" + boundary);
            var uploadRaw = Encoding.UTF8.GetString(_send(req));
            EnsureSuccess(uploadRaw);
            var text = TryExtractText(uploadRaw);
            if (text != null && !string.IsNullOrWhiteSpace(text))
                return text;

            var id = TryExtractId(uploadRaw);
            if (id == null || string.IsNullOrWhiteSpace(id))
                throw new InvalidOperationException("D.Nova OCR upload did not return fileId/taskId or text: " + Truncate(uploadRaw, 300));

            for (var i = 0; i < 30; i++)
            {
                Thread.Sleep(1000);
                var statusUrl = baseUrl + "/queryStatus/" + Uri.EscapeDataString(id);
                using var poll = new HttpRequestMessage(HttpMethod.Post, statusUrl);
                poll.Content = new StringContent("{}", Encoding.UTF8, "application/json");
                var statusRaw = Encoding.UTF8.GetString(_send(poll));
                EnsureSuccess(statusRaw);
                text = TryExtractText(statusRaw);
                if (text != null && !string.IsNullOrWhiteSpace(text))
                    return text;
                var st = FirstMatch(statusRaw, "\"(?:status|state|taskStatus|message)\"\\s*:\\s*\"?([^\",}\\s]+)");
                if (IsFailed(st))
                    throw new InvalidOperationException("D.Nova OCR failed: " + Truncate(statusRaw, 300));
                if (IsDone(st) || AiJson.ParseObject(statusRaw).GetValueOrDefault("processStatus") == "9")
                {
                    // Completion supplies the download task ID, which can differ from the upload ID.
                    return DownloadMarkdown(baseUrl, TryExtractId(statusRaw) ?? id);
                }
            }
            throw new InvalidOperationException("D.Nova OCR timed out waiting for text (fileId=" + id + ").");
        }
    }

    string DownloadMarkdown(string baseUrl, string taskId)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, baseUrl + "/downloadByTaskId");
        req.Content = new StringContent("{\"downloadType\":\"md\",\"taskId\":" + Quote(taskId) + "}", Encoding.UTF8, "application/json");
        var bytes = _send(req);
        using var stream = new MemoryStream(bytes);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        var parts = new List<string>();
        long total = 0;
        foreach (var entry in archive.Entries)
        {
            if (!entry.FullName.EndsWith(".md", StringComparison.OrdinalIgnoreCase)) continue;
            total += entry.Length;
            if (total > 16 * 1024 * 1024)
                throw new InvalidOperationException("D.Nova OCR Markdown exceeds the 16 MB text limit.");
            using var reader = new StreamReader(entry.Open(), Encoding.UTF8);
            parts.Add(reader.ReadToEnd());
        }
        var text = string.Join("\n\n", parts);
        if (string.IsNullOrWhiteSpace(text))
            throw new InvalidOperationException("D.Nova OCR download contains no Markdown text.");
        return text;
    }

    static void EnsureSuccess(string raw)
    {
        var code = AiJson.ParseObject(raw).GetValueOrDefault("code");
        if (!string.IsNullOrEmpty(code) && code != "200" && code != "0")
            throw new InvalidOperationException("D.Nova OCR rejected request (code " + code + ").");
    }

    static byte[] BuildMultipart(string boundary, string fileName, byte[] fileBytes, string fileInfoJson)
    {
        using var ms = new MemoryStream();
        void Write(string s) => ms.Write(Encoding.UTF8.GetBytes(s), 0, Encoding.UTF8.GetByteCount(s));

        Write("--" + boundary + "\r\n");
        Write("Content-Disposition: form-data; name=\"file\"; filename=\"" + fileName.Replace("\"", "") + "\"\r\n");
        Write("Content-Type: " + GuessMime(fileName) + "\r\n\r\n");
        ms.Write(fileBytes, 0, fileBytes.Length);
        Write("\r\n");

        Write("--" + boundary + "\r\n");
        Write("Content-Disposition: form-data; name=\"fileInfo\"\r\nContent-Type: application/json\r\n\r\n");
        Write(fileInfoJson);
        Write("\r\n--" + boundary + "--\r\n");
        return ms.ToArray();
    }

    static string? TryExtractText(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        foreach (var key in new[] { "content", "text", "ocrText", "resultText", "markdown", "md", "result" })
        {
            var m = Regex.Match(raw, "\"" + key + "\"\\s*:\\s*\"((?:\\\\.|[^\"\\\\])*)\"");
            if (m.Success)
            {
                var v = Unescape(m.Groups[1].Value);
                if (v.Length > 20) return v;
            }
        }
        // Sometimes the whole body is markdown/plain text.
        if (!raw.TrimStart().StartsWith("{", StringComparison.Ordinal)
            && !raw.TrimStart().StartsWith("[", StringComparison.Ordinal)
            && raw.Length > 40)
            return raw;
        return null;
    }

    public static string? TryExtractId(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        foreach (var key in new[] { "taskId", "task_id", "fileId", "file_id", "jobId", "job_id", "requestId", "request_id", "data" })
        {
            var quoted = Regex.Match(raw, "\"" + key + "\"\\s*:\\s*\"([^\"]+)\"", RegexOptions.IgnoreCase);
            if (quoted.Success && quoted.Groups[1].Value.Length > 2) return quoted.Groups[1].Value;
            var num = Regex.Match(raw, "\"" + key + "\"\\s*:\\s*([0-9]{3,})", RegexOptions.IgnoreCase);
            if (num.Success) return num.Groups[1].Value;
        }
        return null;
    }

    static string GuessMime(string fileName)
    {
        var n = (fileName ?? "").ToLowerInvariant();
        if (n.EndsWith(".png")) return "image/png";
        if (n.EndsWith(".jpg") || n.EndsWith(".jpeg")) return "image/jpeg";
        if (n.EndsWith(".tif") || n.EndsWith(".tiff")) return "image/tiff";
        if (n.EndsWith(".pdf")) return "application/pdf";
        return "application/octet-stream";
    }

    static bool IsDone(string? status)
        => !string.IsNullOrWhiteSpace(status) && Regex.IsMatch(status, "success|succeed|done|complete|finish|ok", RegexOptions.IgnoreCase);

    static bool IsFailed(string? status)
        => !string.IsNullOrWhiteSpace(status) && Regex.IsMatch(status, "fail|error|cancel", RegexOptions.IgnoreCase);

    static string? FirstMatch(string raw, string pattern)
    {
        var m = Regex.Match(raw, pattern, RegexOptions.IgnoreCase);
        return m.Success ? m.Groups[1].Value : null;
    }

    static byte[] DefaultSend(HttpRequestMessage request)
    {
        ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(90) };
        using var response = client.SendAsync(request).GetAwaiter().GetResult();
        var bytes = response.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult();
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException("D.Nova OCR HTTP " + (int)response.StatusCode + ": " + Truncate(Encoding.UTF8.GetString(bytes), 400));
        return bytes;
    }

    static string Quote(string value) => "\"" + (value ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
    static string Unescape(string value) => value.Replace("\\n", "\n").Replace("\\r", "\r").Replace("\\\"", "\"").Replace("\\\\", "\\");
    static string Truncate(string text, int max) => text.Length <= max ? text : text.Substring(0, max);
}
