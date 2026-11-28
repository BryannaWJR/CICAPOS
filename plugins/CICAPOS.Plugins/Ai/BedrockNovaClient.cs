using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;

namespace CICAPOS.Plugins.Ai;

public interface ILlmClient
{
    string CompleteJson(string systemPrompt, string userPrompt);
}

/// <summary>
/// Amazon Bedrock Converse API → Amazon Nova Micro (amazon.nova-micro-v1:0).
/// POC uses Micro only (cheapest of Nova Micro / Lite / Pro allowed on AWS).
/// </summary>
public sealed class BedrockNovaClient : ILlmClient
{
    readonly AiProviderConfig _config;
    readonly Func<HttpRequestMessage, byte[], string> _send;

    public BedrockNovaClient(AiProviderConfig config, Func<HttpRequestMessage, byte[], string>? send = null)
    {
        _config = config;
        _send = send ?? DefaultSend;
    }

    public string CompleteJson(string systemPrompt, string userPrompt)
    {
        if (!_config.HasBedrock)
            throw new InvalidOperationException("AWS Bedrock Nova Micro is not configured.");

        // Instruct JSON in the prompt — Nova Micro Converse has no OpenAI response_format flag.
        var system = (systemPrompt ?? "").Trim();
        if (system.IndexOf("JSON", StringComparison.OrdinalIgnoreCase) < 0)
            system += " Return ONLY valid JSON.";

        var body = "{"
            + "\"system\":[{\"text\":" + Quote(system) + "}],"
            + "\"messages\":[{\"role\":\"user\",\"content\":[{\"text\":" + Quote(userPrompt) + "}]}],"
            + "\"inferenceConfig\":{\"maxTokens\":4096,\"temperature\":0}"
            + "}";
        var payload = Encoding.UTF8.GetBytes(body);

        var modelPath = "/model/" + Uri.EscapeDataString(_config.ModelId) + "/converse";
        var url = "https://bedrock-runtime." + _config.Region + ".amazonaws.com" + modelPath;
        using var req = new HttpRequestMessage(HttpMethod.Post, url);
        req.Content = new ByteArrayContent(payload);
        req.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");

        AwsSigV4.Sign(req, _config.Region, "bedrock", _config.AccessKeyId!, _config.SecretAccessKey!, _config.SessionToken, payload);

        var raw = _send(req, payload);
        var text = ExtractText(raw);
        if (string.IsNullOrWhiteSpace(text))
            throw new InvalidOperationException("Bedrock Nova Micro returned no text.");
        return ExtractJsonObject(text);
    }

    static string ExtractText(string raw)
    {
        // Converse: output.message.content[].text
        var match = Regex.Match(raw, "\"text\"\\s*:\\s*\"((?:\\\\.|[^\"\\\\])*)\"");
        return match.Success ? Unescape(match.Groups[1].Value) : "";
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

    static string DefaultSend(HttpRequestMessage request, byte[] payload)
    {
        _ = payload;
        ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(45) };
        using var response = client.SendAsync(request).GetAwaiter().GetResult();
        var text = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException("Bedrock HTTP " + (int)response.StatusCode + ": " + Truncate(text, 500));
        return text;
    }

    static string Quote(string value) => "\"" + (value ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "\\r").Replace("\n", "\\n") + "\"";
    static string Unescape(string value) => value.Replace("\\n", "\n").Replace("\\r", "\r").Replace("\\\"", "\"").Replace("\\\\", "\\");
    static string Truncate(string text, int max) => text.Length <= max ? text : text.Substring(0, max);
}
