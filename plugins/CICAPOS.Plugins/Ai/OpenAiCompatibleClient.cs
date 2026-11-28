using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;

namespace CICAPOS.Plugins.Ai;

/// <summary>
/// OpenAI-compatible Chat Completions (Deloitte Nova gateway, DeepSeek, etc.).
/// No AWS account required — only base URL + Bearer API key.
/// </summary>
public sealed class OpenAiCompatibleClient : ILlmClient
{
    readonly AiProviderConfig _config;
    readonly Func<HttpRequestMessage, string> _send;

    public OpenAiCompatibleClient(AiProviderConfig config, Func<HttpRequestMessage, string>? send = null)
    {
        _config = config;
        _send = send ?? DefaultSend;
    }

    public string CompleteJson(string systemPrompt, string userPrompt)
    {
        if (!_config.HasCompatible)
            throw new InvalidOperationException("OpenAI-compatible LLM (Nova/DeepSeek) is not configured.");

        var baseUrl = _config.CompatibleBaseUrl!;
        if (!baseUrl.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
        {
            if (baseUrl.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
                baseUrl += "/chat/completions";
            else
                baseUrl += "/v1/chat/completions";
        }

        var body = "{"
            + "\"model\":" + Quote(_config.CompatibleModel) + ","
            + "\"temperature\":0,"
            + "\"max_tokens\":4096,"
            + "\"messages\":["
            + "{\"role\":\"system\",\"content\":" + Quote(systemPrompt) + "},"
            + "{\"role\":\"user\",\"content\":" + Quote(userPrompt) + "}"
            + "]}";

        using var req = new HttpRequestMessage(HttpMethod.Post, baseUrl);
        req.Headers.TryAddWithoutValidation("Authorization", "Bearer " + _config.CompatibleApiKey);
        req.Content = new StringContent(body, Encoding.UTF8, "application/json");

        var raw = _send(req);
        var content = Regex.Match(raw, "\"content\"\\s*:\\s*\"((?:\\\\.|[^\"\\\\])*)\"").Groups[1].Value;
        if (string.IsNullOrWhiteSpace(content))
            throw new InvalidOperationException("Compatible LLM returned no content.");
        return ExtractJsonObject(Unescape(content));
    }

    static string ExtractJsonObject(string text)
    {
        var trimmed = text.Trim();
        if (trimmed.StartsWith("{", StringComparison.Ordinal)) return trimmed;
        var start = trimmed.IndexOf('{');
        var end = trimmed.LastIndexOf('}');
        if (start >= 0 && end > start) return trimmed.Substring(start, end - start + 1);
        return trimmed;
    }

    static string DefaultSend(HttpRequestMessage request)
    {
        ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(90) };
        using var response = client.SendAsync(request).GetAwaiter().GetResult();
        var text = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException("LLM HTTP " + (int)response.StatusCode + ": " + Truncate(text, 400));
        return text;
    }

    static string Quote(string value) => "\"" + (value ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "\\r").Replace("\n", "\\n") + "\"";
    static string Unescape(string value) => value.Replace("\\n", "\n").Replace("\\r", "\r").Replace("\\\"", "\"").Replace("\\\\", "\\");
    static string Truncate(string text, int max) => text.Length <= max ? text : text.Substring(0, max);
}
