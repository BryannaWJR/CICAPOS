using System.Globalization;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;

namespace CICAPOS.Plugins.Ai;

/// <summary>Minimal AWS Signature Version 4 for Bedrock Runtime Converse.</summary>
static class AwsSigV4
{
    public static void Sign(HttpRequestMessage request, string region, string service, string accessKey, string secretKey, string? sessionToken, byte[] payload)
    {
        var utc = DateTime.UtcNow;
        var amzDate = utc.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
        var dateStamp = utc.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        var host = request.RequestUri!.Host;
        var payloadHash = Hex(Sha256(payload));

        request.Headers.Remove("Host");
        request.Headers.TryAddWithoutValidation("Host", host);
        request.Headers.TryAddWithoutValidation("x-amz-date", amzDate);
        request.Headers.TryAddWithoutValidation("x-amz-content-sha256", payloadHash);
        if (!string.IsNullOrWhiteSpace(sessionToken))
            request.Headers.TryAddWithoutValidation("x-amz-security-token", sessionToken);

        var canonicalHeaders = "content-type:application/json\n"
            + "host:" + host + "\n"
            + "x-amz-content-sha256:" + payloadHash + "\n"
            + "x-amz-date:" + amzDate + "\n"
            + (string.IsNullOrWhiteSpace(sessionToken) ? "" : "x-amz-security-token:" + sessionToken + "\n");
        var signedHeaders = "content-type;host;x-amz-content-sha256;x-amz-date"
            + (string.IsNullOrWhiteSpace(sessionToken) ? "" : ";x-amz-security-token");

        var canonicalRequest = string.Join("\n",
            request.Method.Method,
            request.RequestUri.AbsolutePath,
            "",
            canonicalHeaders,
            signedHeaders,
            payloadHash);

        var credentialScope = dateStamp + "/" + region + "/" + service + "/aws4_request";
        var stringToSign = string.Join("\n",
            "AWS4-HMAC-SHA256",
            amzDate,
            credentialScope,
            Hex(Sha256(Encoding.UTF8.GetBytes(canonicalRequest))));

        var signingKey = GetSignatureKey(secretKey, dateStamp, region, service);
        var signature = Hex(HmacSha256(signingKey, stringToSign));

        var auth = "AWS4-HMAC-SHA256 Credential=" + accessKey + "/" + credentialScope
            + ", SignedHeaders=" + signedHeaders
            + ", Signature=" + signature;
        request.Headers.TryAddWithoutValidation("Authorization", auth);
    }

    static byte[] GetSignatureKey(string key, string dateStamp, string region, string service)
    {
        var kDate = HmacSha256(Encoding.UTF8.GetBytes("AWS4" + key), dateStamp);
        var kRegion = HmacSha256(kDate, region);
        var kService = HmacSha256(kRegion, service);
        return HmacSha256(kService, "aws4_request");
    }

    static byte[] HmacSha256(byte[] key, string data)
    {
        using var hmac = new HMACSHA256(key);
        return hmac.ComputeHash(Encoding.UTF8.GetBytes(data));
    }

    static byte[] Sha256(byte[] data)
    {
        using var sha = SHA256.Create();
        return sha.ComputeHash(data);
    }

    static string Hex(byte[] bytes)
    {
        var sb = new StringBuilder(bytes.Length * 2);
        foreach (var b in bytes) sb.Append(b.ToString("x2", CultureInfo.InvariantCulture));
        return sb.ToString();
    }
}
