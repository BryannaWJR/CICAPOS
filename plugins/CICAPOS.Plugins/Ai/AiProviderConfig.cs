using System.Text.RegularExpressions;

namespace CICAPOS.Plugins.Ai;

/// <summary>
/// D.Nova OCR + Chat (required for live). Demo only when AI_ALLOW_DEMO=true.
/// </summary>
public sealed class AiProviderConfig
{
    public const string DefaultBedrockModelId = "amazon.nova-micro-v1:0";
    public const string DefaultRegion = "us-east-1";
    public const string DefaultNovaChatBase = "https://nova.deloitte.com.cn/v1";
    public const string DefaultNovaOcrBase = "https://nova.deloitte.com.cn/nova-cloud/ocr/ocr/v1";
    public const string DefaultNovaModel = "DeepSeek-V4-Flash";
    public const string DataverseEnvSchema = "cicapos_AiAssistConfig";

    public string? CompatibleBaseUrl { get; init; }
    public string? CompatibleApiKey { get; init; }
    public string CompatibleModel { get; init; } = DefaultNovaModel;

    public string? OcrBaseUrl { get; init; }
    public string? OcrApiKey { get; init; }

    public string Region { get; init; } = DefaultRegion;
    public string ModelId { get; init; } = DefaultBedrockModelId;
    public string? AccessKeyId { get; init; }
    public string? SecretAccessKey { get; init; }
    public string? SessionToken { get; init; }

    /// <summary>When false (default), missing keys or OCR throws instead of using demo catalog.</summary>
    public bool AllowDemo { get; init; }

    public bool HasCompatible =>
        !string.IsNullOrWhiteSpace(CompatibleBaseUrl) && !string.IsNullOrWhiteSpace(CompatibleApiKey);

    public bool HasOcr => !string.IsNullOrWhiteSpace(OcrApiKey);

    public bool HasBedrock =>
        !string.IsNullOrWhiteSpace(AccessKeyId)
        && !string.IsNullOrWhiteSpace(SecretAccessKey)
        && !string.IsNullOrWhiteSpace(ModelId)
        && !string.IsNullOrWhiteSpace(Region);

    public string ActiveProvider =>
        HasCompatible ? "nova-compatible" : HasBedrock ? "bedrock-nova-micro" : AllowDemo ? "demo" : "unconfigured";

    public static AiProviderConfig FromSecureConfig(string? secureConfig)
    {
        var map = ParseSecure(secureConfig);
        string? Env(string key) => First(map, key) ?? Environment.GetEnvironmentVariable(key);

        var chatBase = TrimSlash(Env("NOVA_BASE_URL") ?? Env("OPENAI_BASE_URL") ?? Env("DEEPSEEK_BASE_URL"));
        var chatKey = Env("NOVA_API_KEY") ?? Env("OPENAI_API_KEY") ?? Env("DEEPSEEK_API_KEY");
        if (string.IsNullOrWhiteSpace(chatBase) && !string.IsNullOrWhiteSpace(chatKey))
            chatBase = DefaultNovaChatBase;

        var allowDemo = IsTruthy(Env("AI_ALLOW_DEMO"));

        return new AiProviderConfig
        {
            CompatibleBaseUrl = chatBase,
            CompatibleApiKey = chatKey,
            CompatibleModel = Env("NOVA_MODEL") ?? Env("OPENAI_MODEL") ?? Env("DEEPSEEK_MODEL") ?? DefaultNovaModel,
            OcrBaseUrl = TrimSlash(Env("NOVA_OCR_BASE_URL")) ?? DefaultNovaOcrBase,
            OcrApiKey = Env("NOVA_OCR_API_KEY") ?? Env("NOVA_OCR_KEY"),
            Region = Env("AWS_REGION") ?? Env("AWS_DEFAULT_REGION") ?? Env("BedrockRegion") ?? DefaultRegion,
            ModelId = Env("BEDROCK_MODEL_ID") ?? Env("BedrockModelId") ?? DefaultBedrockModelId,
            AccessKeyId = Env("AWS_ACCESS_KEY_ID") ?? Env("AwsAccessKeyId"),
            SecretAccessKey = Env("AWS_SECRET_ACCESS_KEY") ?? Env("AwsSecretAccessKey"),
            SessionToken = Env("AWS_SESSION_TOKEN") ?? Env("AwsSessionToken"),
            AllowDemo = allowDemo
        };
    }

    static bool IsTruthy(string? v)
        => string.Equals(v, "1", StringComparison.OrdinalIgnoreCase)
           || string.Equals(v, "true", StringComparison.OrdinalIgnoreCase)
           || string.Equals(v, "yes", StringComparison.OrdinalIgnoreCase);

    static Dictionary<string, string> ParseSecure(string? secure)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(secure)) return map;
        foreach (Match match in Regex.Matches(secure, "\"([^\"]+)\"\\s*:\\s*\"((?:\\\\.|[^\"\\\\])*)\""))
            map[match.Groups[1].Value] = match.Groups[2].Value.Replace("\\\"", "\"").Replace("\\\\", "\\");
        foreach (var part in secure.Split(new[] { ';', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var i = part.IndexOf('=');
            if (i <= 0) continue;
            map[part.Substring(0, i).Trim()] = part.Substring(i + 1).Trim().Trim('"');
        }
        return map;
    }

    static string? First(Dictionary<string, string> map, string key)
        => map.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : null;

    static string? TrimSlash(string? url)
        => string.IsNullOrWhiteSpace(url) ? url : url.TrimEnd('/');
}
