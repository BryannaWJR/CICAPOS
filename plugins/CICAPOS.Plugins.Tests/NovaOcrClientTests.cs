using System.IO.Compression;
using System.Net.Http;
using System.Text;
using CICAPOS.Plugins.Ai;
using Xunit;

namespace CICAPOS.Plugins.Tests;

public class NovaOcrClientTests
{
    static byte[] Utf8(string text) => Encoding.UTF8.GetBytes(text);

    static byte[] Zip(string name, string text)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, true))
        using (var writer = new StreamWriter(archive.CreateEntry(name).Open()))
            writer.Write(text);
        return stream.ToArray();
    }

    [Theory]
    [InlineData("{\"code\":200,\"data\":\"2124872743889522690\"}")]
    [InlineData("{\"code\":200,\"data\":2124872743889522690}")]
    public void UploadReadsScalarDataWithoutLosingIdPrecision(string json)
        => Assert.Equal("2124872743889522690", NovaOcrClient.TryExtractId(json));

    [Fact]
    public void RealResponseFlowPollsThenExtractsMarkdownZip()
    {
        var calls = 0;
        byte[] Send(HttpRequestMessage request)
        {
            calls++;
            var path = request.RequestUri!.AbsolutePath;
            Assert.Equal("ocr.example.test", request.RequestUri.Host);
            if (calls == 1)
            {
                Assert.EndsWith("/callOcrFileParse", path);
                Assert.Contains("Content-Type: application/json", request.Content!.ReadAsStringAsync().Result);
                return Utf8("{\"code\":200,\"message\":\"ok\",\"data\":\"2124872743889522690\"}");
            }
            if (calls == 2)
            {
                Assert.EndsWith("/queryStatus/2124872743889522690", path);
                return Utf8("{\"code\":\"200\",\"message\":\"Processing\",\"processStatus\":\"1\"}");
            }
            if (calls == 3)
                return Utf8("{\"code\":\"200\",\"message\":\"Completed\",\"processStatus\":\"9\",\"taskId\":\"download-123\",\"bizStatus\":5}");
            Assert.Equal(4, calls);
            Assert.EndsWith("/downloadByTaskId", path);
            Assert.Contains("\"taskId\":\"download-123\"", request.Content!.ReadAsStringAsync().Result);
            return Zip("output/document.md", "Invoice total: HKD 1234.50\n測試");
        }
        var client = new NovaOcrClient(new AiProviderConfig
        {
            OcrApiKey = "sk-test", OcrBaseUrl = "https://ocr.example.test/ocr/v1"
        }, Send);
        Assert.Equal("Invoice total: HKD 1234.50\n測試", client.ExtractText(new byte[] { 1 }, "test.pdf"));
        Assert.Equal(4, calls);
    }

    [Fact]
    public void ApiFailureDoesNotTreatDataAsSuccessfulJob()
    {
        var client = new NovaOcrClient(new AiProviderConfig { OcrApiKey = "sk-test" },
            _ => Utf8("{\"code\":401,\"data\":\"invalid-key\"}"));
        Assert.Contains("code 401", Assert.Throws<InvalidOperationException>(
            () => client.ExtractText(new byte[] { 1 }, "test.pdf")).Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DownloadFailuresAreNotReportedAsPollingTimeouts(bool missingMarkdown)
    {
        var calls = 0;
        byte[] Send(HttpRequestMessage request)
        {
            if (++calls == 1) return Utf8("{\"code\":200,\"data\":\"123456\"}");
            if (calls == 2) return Utf8("{\"message\":\"Completed\",\"processStatus\":\"9\",\"taskId\":\"123456\"}");
            if (missingMarkdown) return Zip("image.png", "not markdown");
            throw new HttpRequestException("Download HTTP 503");
        }
        var client = new NovaOcrClient(new AiProviderConfig { OcrApiKey = "sk-test" }, Send);
        var error = Record.Exception(() => client.ExtractText(new byte[] { 1 }, "test.pdf"));
        Assert.NotNull(error);
        Assert.Contains(missingMarkdown ? "no Markdown" : "HTTP 503", error.Message);
    }
}
