using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;

namespace CICAPOS.Plugins.Ai;

public interface IWebPageClient
{
    string FetchText(string url);
}

/// <summary>
/// Fetches a public product webpage and returns visible text for LLM extraction.
/// </summary>
public sealed class WebPageClient : IWebPageClient
{
    readonly Func<HttpRequestMessage, string> _send;

    public WebPageClient(Func<HttpRequestMessage, string>? send = null)
        => _send = send ?? DefaultSend;

    public string FetchText(string url)
    {
        if (!TryNormalizeUrl(url, out var uri))
            throw new InvalidOperationException("Product webpage URL is not a valid http(s) address.");

        using var req = new HttpRequestMessage(HttpMethod.Get, uri);
        req.Headers.TryAddWithoutValidation("User-Agent", "CICAPOS-AI/1.0 (CITF prefill)");
        req.Headers.TryAddWithoutValidation("Accept", "text/html,application/xhtml+xml,text/plain");
        var raw = _send(req);
        var text = StripHtml(raw);
        if (string.IsNullOrWhiteSpace(text) || text.Length < 40)
            throw new InvalidOperationException("Product webpage did not return readable text.");
        return text.Length > 12000 ? text.Substring(0, 12000) : text;
    }

    public static bool TryNormalizeUrl(string? url, out Uri uri)
    {
        uri = null!;
        var raw = (url ?? "").Trim();
        if (raw.Length == 0) return false;
        if (raw.IndexOf("://", StringComparison.Ordinal) < 0)
            raw = "https://" + raw;
        if (!Uri.TryCreate(raw, UriKind.Absolute, out var parsed))
            return false;
        if (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps)
            return false;
        uri = parsed;
        return true;
    }

    public static string StripHtml(string html)
    {
        if (string.IsNullOrWhiteSpace(html)) return "";
        var t = Regex.Replace(html, "(?is)<script[^>]*>.*?</script>", " ");
        t = Regex.Replace(t, "(?is)<style[^>]*>.*?</style>", " ");
        t = Regex.Replace(t, "(?is)<noscript[^>]*>.*?</noscript>", " ");
        t = Regex.Replace(t, "(?is)<!--.*?-->", " ");
        t = Regex.Replace(t, "(?is)<[^>]+>", " ");
        t = WebUtility.HtmlDecode(t);
        t = Regex.Replace(t, @"\s+", " ").Trim();
        return t;
    }

    static string DefaultSend(HttpRequestMessage request)
    {
        ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(25) };
        using var response = client.SendAsync(request).GetAwaiter().GetResult();
        var text = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException("Webpage HTTP " + (int)response.StatusCode + ": " + Truncate(text, 200));
        return text;
    }

    static string Truncate(string text, int max)
        => string.IsNullOrEmpty(text) || text.Length <= max ? text ?? "" : text.Substring(0, max);
}
