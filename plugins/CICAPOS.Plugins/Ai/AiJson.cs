using System.Collections;
using System.Globalization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Text.RegularExpressions;

namespace CICAPOS.Plugins.Ai;

public static class AiJson
{
    public static string Serialize<T>(T value)
    {
        var serializer = new DataContractJsonSerializer(typeof(T));
        using var stream = new MemoryStream();
        serializer.WriteObject(stream, value);
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    public static T? Deserialize<T>(string? json) where T : class
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        var serializer = new DataContractJsonSerializer(typeof(T));
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        return serializer.ReadObject(stream) as T;
    }

    public static Dictionary<string, string> ParseObject(string? json)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(json)) return map;
        foreach (Match match in Regex.Matches(json, "\"([^\"]+)\"\\s*:\\s*(null|true|false|\"((?:\\\\.|[^\"\\\\])*)\"|-?\\d+(?:\\.\\d+)?)"))
        {
            var key = match.Groups[1].Value;
            if (match.Groups[2].Value is "null")
                map[key] = "";
            else if (match.Groups[3].Success)
                map[key] = Unescape(match.Groups[3].Value);
            else
                map[key] = match.Groups[2].Value;
        }
        return map;
    }

    public static string ToObjectJson(IDictionary<string, string> fields)
    {
        var parts = new List<string>();
        foreach (var pair in fields)
        {
            var key = Escape(pair.Key);
            var raw = pair.Value ?? "";
            if (raw is "true" or "false" || LooksNumber(raw))
                parts.Add("\"" + key + "\":" + raw);
            else
                parts.Add("\"" + key + "\":\"" + Escape(raw) + "\"");
        }
        return "{" + string.Join(",", parts) + "}";
    }

    public static string ToArrayJson(IEnumerable<AiIssue> issues)
        => "[" + string.Join(",", issues.Select(issue =>
            "{\"field\":\"" + Escape(issue.Field) + "\",\"code\":\"" + Escape(issue.Code)
            + "\",\"message\":\"" + Escape(issue.Message) + "\",\"severity\":\"" + Escape(issue.Severity) + "\"}")) + "]";

    public static List<AiIssue> ParseIssues(string? json)
    {
        var list = new List<AiIssue>();
        if (string.IsNullOrWhiteSpace(json)) return list;
        foreach (Match block in Regex.Matches(json, "\\{[^{}]+\\}"))
        {
            var map = ParseObject(block.Value);
            if (map.Count == 0) continue;
            list.Add(new AiIssue
            {
                Field = map.GetValueOrDefault("field") ?? "",
                Code = map.GetValueOrDefault("code") ?? "",
                Message = map.GetValueOrDefault("message") ?? "",
                Severity = map.GetValueOrDefault("severity") ?? "error"
            });
        }
        return list;
    }

    public static string Sha256Hex(string text)
    {
        using var sha = System.Security.Cryptography.SHA256.Create();
        var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(text ?? ""));
        var sb = new StringBuilder(bytes.Length * 2);
        foreach (var b in bytes) sb.Append(b.ToString("x2", CultureInfo.InvariantCulture));
        return sb.ToString();
    }

    static bool LooksNumber(string raw)
        => decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out _);

    static string Escape(string value)
        => (value ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "\\r").Replace("\n", "\\n");

    static string Unescape(string value)
        => value.Replace("\\n", "\n").Replace("\\r", "\r").Replace("\\\"", "\"").Replace("\\\\", "\\");
}

public static class DictExt
{
    public static string? GetValueOrDefault(this IDictionary<string, string> map, string key)
        => map != null && map.TryGetValue(key, out var value) ? value : null;
}
