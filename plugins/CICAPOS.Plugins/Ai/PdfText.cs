using System.Text;
using System.Text.RegularExpressions;

namespace CICAPOS.Plugins.Ai;

/// <summary>
/// Pulls literal Tj strings from simple generated PDFs so signature markers
/// can be checked without waiting on OCR.
/// </summary>
public static class PdfText
{
    public static string Extract(byte[]? bytes)
    {
        if (bytes == null || bytes.Length < 5) return "";
        if (bytes[0] != (byte)'%' || bytes[1] != (byte)'P' || bytes[2] != (byte)'D' || bytes[3] != (byte)'F')
            return "";
        var raw = Encoding.GetEncoding(28591).GetString(bytes);
        var sb = new StringBuilder();
        foreach (Match m in Regex.Matches(raw, @"\((?:\\.|[^\\)])*\)\s*Tj"))
        {
            var inner = m.Value;
            var close = inner.LastIndexOf(')');
            if (close <= 0) continue;
            inner = inner.Substring(1, close - 1)
                .Replace("\\n", "\n")
                .Replace("\\r", "")
                .Replace("\\t", "\t")
                .Replace("\\(", "(")
                .Replace("\\)", ")")
                .Replace("\\\\", "\\");
            if (sb.Length > 0) sb.Append('\n');
            sb.Append(inner);
        }
        return sb.ToString();
    }
}
