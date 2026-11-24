using System.Net.Http;
using CICAPOS.Plugins.Ai;
using Xunit;

public class AiAssistTests
{
    static AiAssistService DemoService() => new(AiProviderConfig.FromSecureConfig("AI_ALLOW_DEMO=true"));

    [Fact]
    public void Prefill_CompanyRegistration_ReturnsBrAndBankFields()
    {
        var res = DemoService().Execute(new AiAssistRequest
        {
            Action = AiActions.Prefill,
            SchemaKey = AiSchemaKeys.CompanyRegistration
        });
        Assert.True(res.Pass);
        Assert.Equal("demo", res.Source);
        var fields = AiJson.ParseObject(res.FieldsJson);
        Assert.Equal("68899001", fields["br"]);
        Assert.False(string.IsNullOrWhiteSpace(fields["bankAcct"]));
    }

    [Fact]
    public void Prefill_CitfSite_ReturnsContractFields()
    {
        var res = DemoService().Execute(new AiAssistRequest
        {
            Action = AiActions.Prefill,
            SchemaKey = AiSchemaKeys.CitfSite
        });
        var fields = AiJson.ParseObject(res.FieldsJson);
        Assert.Equal("C/2027/031", fields["no"]);
        Assert.Contains("Tung Chung", fields["n"]);
    }

    [Fact]
    public void Prefill_PalProduct_FillsCoreListingFields()
    {
        var res = DemoService().Execute(new AiAssistRequest
        {
            Action = AiActions.Prefill,
            SchemaKey = AiSchemaKeys.PalProduct
        });
        var fields = AiJson.ParseObject(res.FieldsJson);
        Assert.Equal("86000", fields["price"]);
        Assert.Contains("SiteEye", fields["name"]);
    }

    [Fact]
    public void ValidateForm_RejectsBriefObjective()
    {
        var res = DemoService().Execute(new AiAssistRequest
        {
            Action = AiActions.ValidateForm,
            SchemaKey = AiSchemaKeys.FundingApplication,
            CurrentValuesJson = "{\"objective\":\"Too short\",\"itemsJson\":\"[{\\\"plan\\\":\\\"Cover 4 zones\\\"}]\"}"
        });
        Assert.False(res.Pass);
        var issues = AiJson.ParseIssues(res.IssuesJson);
        Assert.Contains(issues, i => i.Code == "too_brief");
    }

    [Fact]
    public void ValidateForm_RejectsObjectiveOverMaxWords()
    {
        var longObj = string.Join(" ", Enumerable.Repeat("word", 251));
        var res = DemoService().Execute(new AiAssistRequest
        {
            Action = AiActions.ValidateForm,
            SchemaKey = AiSchemaKeys.FundingApplication,
            CurrentValuesJson = "{\"objective\":\"" + longObj + "\",\"itemsJson\":\"[{\\\"plan\\\":\\\"Monitor 4 tower crane zones\\\"}]\"}"
        });
        Assert.False(res.Pass);
        Assert.Contains(AiJson.ParseIssues(res.IssuesJson), i => i.Code == "too_long");
    }

    [Fact]
    public void ValidateForm_RejectsPlanWithoutQuantity()
    {
        var res = DemoService().Execute(new AiAssistRequest
        {
            Action = AiActions.ValidateForm,
            SchemaKey = AiSchemaKeys.FundingApplication,
            CurrentValuesJson = "{\"objective\":\"Deploy cameras on site to cut struck-by incidents and improve safety for workers during lifting.\",\"itemsJson\":\"[{\\\"plan\\\":\\\"Monitor proximity on site\\\"}]\"}"
        });
        Assert.False(res.Pass);
        var issues = AiJson.ParseIssues(res.IssuesJson);
        Assert.Contains(issues, i => i.Code == "plan_no_quantity");
    }

    [Fact]
    public void ValidateForm_AcceptsSeededHarbourObjectiveAndPlans()
    {
        var res = DemoService().Execute(new AiAssistRequest
        {
            Action = AiActions.ValidateForm,
            SchemaKey = AiSchemaKeys.FundingApplication,
            CurrentValuesJson = "{\"objective\":\"Cut struck-by incidents on four tower-crane zones and automate podium rebar tying to save about 30% of fixing labour.\",\"itemsJson\":\"[{\\\"plan\\\":\\\"Monitor plant-person proximity on 4 tower crane zones.\\\"},{\\\"plan\\\":\\\"Automate rebar tying on 3 podium slabs (local development declaration attached).\\\"}]\"}"
        });
        Assert.True(res.Pass);
        Assert.DoesNotContain(AiJson.ParseIssues(res.IssuesJson), i => i.Severity == "error");
    }

    [Fact]
    public void ValidateUpload_QuotationMismatch_FlagsAmount()
    {
        var res = DemoService().Execute(new AiAssistRequest
        {
            Action = AiActions.ValidateUpload,
            SchemaKey = AiSchemaKeys.QuotationCheck,
            DocType = "npa",
            CurrentValuesJson = "{\"formAmount\":\"420000\"}"
        });
        Assert.False(res.Pass);
        Assert.Contains(AiJson.ParseIssues(res.IssuesJson), i => i.Code == "mismatch");
    }

    [Fact]
    public void ValidateUpload_BlurredScan_FlagsReadable()
    {
        var res = DemoService().Execute(new AiAssistRequest
        {
            Action = AiActions.ValidateUpload,
            SchemaKey = AiSchemaKeys.SupportingDoc,
            DocType = "blurred"
        });
        Assert.False(res.Pass);
        Assert.Contains(AiJson.ParseIssues(res.IssuesJson), i => i.Code == "blurred");
    }

    [Fact]
    public void AllSchemas_AreRegistered()
    {
        Assert.Contains(AiFormSchemas.List(), s => s.Key == AiSchemaKeys.CompanyRegistration);
        Assert.Contains(AiFormSchemas.List(), s => s.Key == AiSchemaKeys.PalProduct);
        Assert.Contains(AiFormSchemas.List(), s => s.Key == AiSchemaKeys.FundingApplication);
        Assert.Contains(AiFormSchemas.List(), s => s.Key == AiSchemaKeys.DisbursementRequest);
        Assert.Contains(AiFormSchemas.List(), s => s.Key == AiSchemaKeys.AnnualConfirmation);
        Assert.Equal(11, AiFormSchemas.List().Count());
    }

    [Fact]
    public void Config_HasOcr_WhenNovaOcrKeyPresent()
    {
        var cfg = AiProviderConfig.FromSecureConfig("NOVA_OCR_API_KEY=sk-test-ocr");
        Assert.True(cfg.HasOcr);
        Assert.Equal(AiProviderConfig.DefaultNovaOcrBase, cfg.OcrBaseUrl);
    }

    [Fact]
    public void Prefill_WithOcrAndLlm_MapsDocumentToFields()
    {
        var cfg = new AiProviderConfig
        {
            CompatibleBaseUrl = "https://example.test/v1",
            CompatibleApiKey = "ak-test",
            CompatibleModel = "DeepSeek-V4-Flash",
            OcrApiKey = "sk-test"
        };
        var ocr = new FakeOcr("Business Registration No. 68899001\nCompany: Lantau Smart Construction Limited");
        var llm = new FakeLlm("{\"br\":\"68899001\",\"nameEn\":\"Lantau Smart Construction Limited\",\"bankAcct\":\"511-278830-838\"}");
        var svc = new AiAssistService(cfg, llm, ocr);
        var res = svc.Execute(new AiAssistRequest
        {
            Action = AiActions.Prefill,
            SchemaKey = AiSchemaKeys.CompanyRegistration,
            FileName = "BR.pdf",
            FileBase64 = Convert.ToBase64String(new byte[] { 1, 2, 3, 4 })
        });
        Assert.Equal("nova-ocr+llm", res.Source);
        Assert.Equal("DeepSeek-V4-Flash", res.Model);
        var fields = AiJson.ParseObject(res.FieldsJson);
        Assert.Equal("68899001", fields["br"]);
        Assert.True(ocr.Called);
        Assert.True(llm.Called);
    }

    [Fact]
    public void Prefill_FileWithoutOcrKey_FallsBackToDemo_OnlyWhenAllowed()
    {
        var res = DemoService().Execute(new AiAssistRequest
        {
            Action = AiActions.Prefill,
            SchemaKey = AiSchemaKeys.CitfSite,
            FileName = "award.pdf",
            FileBase64 = Convert.ToBase64String(new byte[] { 9, 9, 9 })
        });
        Assert.Equal("demo", res.Source);
        Assert.Contains("NOVA_OCR_API_KEY", res.Notes ?? "");
    }

    [Fact]
    public void Prefill_FileWithoutOcrKey_ThrowsWhenLiveRequired()
    {
        var cfg = new AiProviderConfig
        {
            CompatibleBaseUrl = "https://example.test/v1",
            CompatibleApiKey = "ak-test",
            AllowDemo = false
        };
        var svc = new AiAssistService(cfg, new FakeLlm("{}"), ocr: null);
        Assert.Throws<InvalidOperationException>(() => svc.Execute(new AiAssistRequest
        {
            Action = AiActions.Prefill,
            SchemaKey = AiSchemaKeys.CitfSite,
            FileName = "award.pdf",
            FileBase64 = Convert.ToBase64String(new byte[] { 9, 9, 9 })
        }));
    }

    [Fact]
    public void Ocr_ParsesNestedTaskIdThenPollsText()
    {
        var n = 0;
        byte[] Send(HttpRequestMessage req)
        {
            n++;
            if (n == 1) return System.Text.Encoding.UTF8.GetBytes("{\"code\":0,\"data\":{\"taskId\":\"t-99\"}}");
            return System.Text.Encoding.UTF8.GetBytes("{\"status\":\"success\",\"content\":\"Business Registration No. 68899001 extra sample text.\"}");
        }
        var client = new NovaOcrClient(new AiProviderConfig { OcrApiKey = "sk-test" }, Send);
        var text = client.ExtractText(new byte[] { 1, 2, 3, 4 }, "br.pdf");
        Assert.Contains("68899001", text);
        Assert.Equal(2, n);
    }

    [Fact]
    public void Ocr_ExtractsTaskIdFromNestedJson()
    {
        Assert.Equal("t-99", NovaOcrClient.TryExtractId("{\"code\":0,\"data\":{\"taskId\":\"t-99\"}}"));
    }

    [Fact]
    public void LiveConfig_DisallowsDemoByDefault()
    {
        var cfg = AiProviderConfig.FromSecureConfig(null);
        Assert.False(cfg.AllowDemo);
        Assert.Equal("unconfigured", cfg.ActiveProvider);
    }

    [Fact]
    public void ValidateUpload_PalMatrixDoc_ReturnsOcrNote()
    {
        var res = DemoService().Execute(new AiAssistRequest
        {
            Action = AiActions.ValidateUpload,
            SchemaKey = AiSchemaKeys.SupportingDoc,
            DocType = "Authorisation letter"
        });
        Assert.True(res.Pass);
        var fields = AiJson.ParseObject(res.FieldsJson);
        Assert.Contains("OCR", fields["note"]);
    }

    [Fact]
    public void ValidateForm_Disbursement_FlagsMissingDocs()
    {
        var res = DemoService().Execute(new AiAssistRequest
        {
            Action = AiActions.ValidateForm,
            SchemaKey = AiSchemaKeys.DisbursementRequest,
            CurrentValuesJson = "{\"docsJson\":\"[]\",\"paid\":\"true\"}"
        });
        Assert.False(res.Pass);
        Assert.Contains(AiJson.ParseIssues(res.IssuesJson), i => i.Code == "missing");
    }

    [Fact]
    public void ValidateForm_AnnualConfirmation_AcceptsCompleteSurvey()
    {
        var res = DemoService().Execute(new AiAssistRequest
        {
            Action = AiActions.ValidateForm,
            SchemaKey = AiSchemaKeys.AnnualConfirmation,
            CurrentValuesJson = "{\"hasNc\":\"No\",\"prod\":\"4\",\"safety\":\"5\",\"quality\":\"3\"}"
        });
        Assert.True(res.Pass);
    }

    [Fact]
    public void Prefill_FromProductWebpage_MapsListingFields()
    {
        var cfg = new AiProviderConfig
        {
            CompatibleBaseUrl = "https://example.test/v1",
            CompatibleApiKey = "ak-test",
            CompatibleModel = "DeepSeek-V4-Flash"
        };
        var llm = new FakeLlm("{\"name\":\"SiteEye AI Safety Camera\",\"model\":\"SE-400\",\"spec\":\"IP66 edge AI camera\",\"mf\":\"SmartSite\",\"price\":\"86000\"}");
        var web = new FakeWeb("SiteEye AI Safety Camera SE-400 IP66 helmet detection HK$86,000 manufacturer SmartSite Robotics.");
        var svc = new AiAssistService(cfg, llm, ocr: null, web: web);
        var res = svc.Execute(new AiAssistRequest
        {
            Action = AiActions.Prefill,
            SchemaKey = AiSchemaKeys.PalProduct,
            CurrentValuesJson = "{\"pageUrl\":\"https://smartsite.example.com/se-400\"}"
        });
        Assert.Contains("Webpage", res.Notes ?? "");
        Assert.Equal("SiteEye AI Safety Camera", AiJson.ParseObject(res.FieldsJson)["name"]);
        Assert.True(web.Called);
        Assert.True(llm.Called);
    }

    [Fact]
    public void Summarize_Demo_ReturnsSubmissionSummary()
    {
        var res = DemoService().Execute(new AiAssistRequest
        {
            Action = AiActions.Summarize,
            SchemaKey = AiSchemaKeys.FundingApplication,
            CurrentValuesJson = "{\"project\":\"Tung Chung Site 3\",\"schemes\":\"ACT\",\"objective\":\"Cut struck-by incidents on four tower-crane zones and automate podium rebar tying.\",\"itemsJson\":\"[{\\\"product\\\":\\\"SiteEye\\\"}]\",\"amount\":\"764000\"}"
        });
        Assert.True(res.Pass);
        var fields = AiJson.ParseObject(res.FieldsJson);
        Assert.False(string.IsNullOrWhiteSpace(fields["summary"]));
        Assert.Contains("ACT", fields["summary"]);
    }

    sealed class FakeWeb : IWebPageClient
    {
        readonly string _text;
        public bool Called { get; private set; }
        public FakeWeb(string text) => _text = text;
        public string FetchText(string url)
        {
            Called = true;
            return _text;
        }
    }

    sealed class FakeOcr : INovaOcrClient
    {
        readonly string _text;
        public bool Called { get; private set; }
        public FakeOcr(string text) => _text = text;
        public string ExtractText(byte[] fileBytes, string? fileName)
        {
            Called = true;
            return _text;
        }
    }

    sealed class FakeLlm : ILlmClient
    {
        readonly string _json;
        public bool Called { get; private set; }
        public FakeLlm(string json) => _json = json;
        public string CompleteJson(string systemPrompt, string userPrompt)
        {
            Called = true;
            return _json;
        }
    }
}
