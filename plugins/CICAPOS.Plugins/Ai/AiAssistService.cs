using System.Text.RegularExpressions;

namespace CICAPOS.Plugins.Ai;

public sealed class AiAssistService
{
    readonly AiProviderConfig _config;
    readonly ILlmClient? _llm;
    readonly INovaOcrClient? _ocr;
    readonly IWebPageClient? _web;

    public AiAssistService(AiProviderConfig config, ILlmClient? llm = null, INovaOcrClient? ocr = null, IWebPageClient? web = null)
    {
        _config = config;
        _llm = llm ?? CreateLlm(config);
        _ocr = ocr ?? (config.HasOcr ? new NovaOcrClient(config) : null);
        _web = web ?? new WebPageClient();
    }

    static ILlmClient? CreateLlm(AiProviderConfig config)
    {
        if (config.HasCompatible) return new OpenAiCompatibleClient(config);
        if (config.HasBedrock) return new BedrockNovaClient(config);
        return null;
    }

    string? LiveModelId() =>
        _config.HasCompatible ? _config.CompatibleModel : _config.HasBedrock ? _config.ModelId : null;

    string LiveSource() =>
        _config.HasCompatible ? "nova-compatible" : "bedrock-nova-micro";

    public AiAssistResponse Execute(AiAssistRequest request)
    {
        if (request == null) throw new ArgumentNullException(nameof(request));
        var action = (request.Action ?? "").Trim().ToLowerInvariant();
        var schema = AiFormSchemas.Get(request.SchemaKey);
        var current = AiJson.ParseObject(request.CurrentValuesJson);
        var hash = request.ContentHash;
        if (string.IsNullOrWhiteSpace(hash))
            hash = AiJson.Sha256Hex((request.DocumentText ?? "") + "|" + (request.FileBase64 ?? "").Length + "|" + (request.CurrentValuesJson ?? "") + "|" + action + "|" + schema.Key);
        hash ??= "";

        var response = action switch
        {
            AiActions.Prefill => Prefill(request, schema, hash),
            AiActions.ValidateUpload => ValidateUpload(request, schema, current, hash),
            AiActions.ValidateForm => ValidateForm(request, schema, current, hash),
            AiActions.Suggest => Suggest(request, current, hash),
            AiActions.Summarize => Summarize(current, hash),
            _ => throw new ArgumentException("Unsupported AI action: " + request.Action)
        };
        if (AiQuality.IsTraditional(current)) ChineseScript.Apply(response);
        return response;
    }

    AiAssistResponse Prefill(AiAssistRequest request, AiFormSchema schema, string hash)
    {
        var documentText = ResolveDocumentText(request, out var usedOcr);
        var hadFile = Decode(request.FileBase64).Length > 0;

        if (_llm != null && !string.IsNullOrWhiteSpace(documentText))
        {
            var current = AiJson.ParseObject(request.CurrentValuesJson);
            var json = _llm.CompleteJson(
                "You extract CITF portal form fields. Return a flat JSON object only.",
                AiQuality.BuildPrefillPrompt(documentText, schema) + AiQuality.PortalLanguageInstruction(current));
            var fields = AiJson.ParseObject(json);
            var source = usedOcr ? "nova-ocr+llm" : LiveSource();
            var issues = AiQuality.ValidateMappedFields(schema, fields).ToList();
            return Ok(fields, issues, source, LiveModelId(), hash,
                (usedOcr ? "Document" : PageUrl(request) != null ? "Webpage" : "Text")
                + " read and fields filled (" + source + ")");
        }

        if (!_config.AllowDemo)
            RequireLlmForMapping(documentText, hadFile);

        var demo = AiDemoCatalog.Prefill(schema.Key, request.DocType);
        foreach (var pair in AiJson.ParseObject(request.CurrentValuesJson))
            if (!string.IsNullOrWhiteSpace(pair.Value) && !demo.ContainsKey(pair.Key))
                demo[pair.Key] = pair.Value;
        return Ok(demo, AiQuality.ValidateMappedFields(schema, demo).ToList(), "demo", null, hash,
            hadFile && _ocr == null
                ? "File received but NOVA_OCR_API_KEY not set – demo catalog used"
                : "Prefill for " + schema.Title);
    }

    AiAssistResponse ValidateUpload(AiAssistRequest request, AiFormSchema schema, Dictionary<string, string> current, string hash)
    {
        if (string.Equals(request.DocType, "blurred", StringComparison.OrdinalIgnoreCase))
        {
            var blur = AiDemoCatalog.Prefill(AiSchemaKeys.SupportingDoc, "blurred");
            var blurIssues = AiQuality.ValidateSupportingDoc(blur).ToList();
            return Ok(blur, blurIssues, LiveSource(), LiveModelId(), hash,
                blur.GetValueOrDefault("note") ?? "Image is blurred – text cannot be read. Please upload a clearer copy.");
        }

        string documentText;
        bool usedOcr;
        try
        {
            documentText = ResolveDocumentText(request, out usedOcr);
        }
        catch (Exception ex) when (NovaOcrClient.IsBusy(ex.Message))
        {
            return OcrBusyFallback(request, current, hash, ex.Message);
        }
        var hadFile = Decode(request.FileBase64).Length > 0;

        if (AiQuality.IsSignedApplicationForm(request.DocType))
        {
            var signedIssues = AiQuality.ValidateSignedForm(request.FileName, documentText, current).ToList();
            if (_llm != null && !string.IsNullOrWhiteSpace(documentText))
            {
                try
                {
                    var json = _llm.CompleteJson(
                        "Check a signed CITF application form. Return JSON: hasSignature (true/false), hasChop (true/false), matchesGenerated (true/false), note.",
                        "Expected: " + AiJson.ToObjectJson(current) + "\nFile: " + (request.FileName ?? "") + "\nText:\n" + Truncate(documentText, 4000) + AiQuality.PortalLanguageInstruction(current));
                    var fields = AiJson.ParseObject(json);
                    if (string.Equals(fields.GetValueOrDefault("hasSignature"), "true", StringComparison.OrdinalIgnoreCase)
                        && !Regex.IsMatch(documentText + request.FileName, @"\[\[UNSIGNED-SIGNATURE\]\]|Signature:\s*_{4,}", RegexOptions.IgnoreCase))
                        signedIssues.RemoveAll(i => i.Code == "missing_signature");
                    if (string.Equals(fields.GetValueOrDefault("hasChop"), "true", StringComparison.OrdinalIgnoreCase)
                        && !Regex.IsMatch(documentText + request.FileName, @"\[\[UNSIGNED-CHOP\]\]|Company chop:\s*_{4,}", RegexOptions.IgnoreCase))
                        signedIssues.RemoveAll(i => i.Code == "missing_chop");
                    if (string.Equals(fields.GetValueOrDefault("matchesGenerated"), "true", StringComparison.OrdinalIgnoreCase))
                        signedIssues.RemoveAll(i => i.Code == "form_mismatch");
                    var note = signedIssues.Count == 0
                        ? (fields.GetValueOrDefault("note") ?? "Signed form matches the generated application and chop/signature are present")
                        : signedIssues[0].Message;
                    return Ok(fields, signedIssues, usedOcr ? "nova-ocr+llm" : LiveSource(), LiveModelId(), hash, note);
                }
                catch when (_config.AllowDemo) { /* fall through to rules */ }
            }
            if (!_config.AllowDemo && _llm == null)
                RequireLlmForMapping(documentText, hadFile);
            var signedNote = signedIssues.Count == 0
                ? "Signed form matches the generated application and chop/signature are present"
                : signedIssues[0].Message;
            return Ok(current, signedIssues, _llm == null ? "rules" : LiveSource(), LiveModelId(), hash, signedNote);
        }

        if (schema.Key.Equals(AiSchemaKeys.QuotationCheck, StringComparison.OrdinalIgnoreCase))
        {
            if (_llm != null && !string.IsNullOrWhiteSpace(documentText))
            {
                var prompt = AiQuality.BuildPrefillPrompt(documentText, schema)
                    + "\nForm amount to compare: " + (current.GetValueOrDefault("formAmount") ?? "");
                var fields = AiJson.ParseObject(_llm.CompleteJson(
                    "Extract quotation supplier, product, qty, unit price and total amount. Return JSON only.",
                    prompt + AiQuality.PortalLanguageInstruction(current)));
                if (!fields.ContainsKey("formAmount") && current.ContainsKey("formAmount"))
                    fields["formAmount"] = current["formAmount"];
                var issues = AiQuality.ValidateQuotation(fields).ToList();
                return Ok(fields, issues, usedOcr ? "nova-ocr+llm" : LiveSource(), LiveModelId(), hash,
                    issues.Count == 0 ? "Quotation matches the form" : "Quotation flagged");
            }

            if (!_config.AllowDemo)
                RequireLlmForMapping(documentText, hadFile);

            var demoQ = AiDemoCatalog.Prefill(AiSchemaKeys.QuotationCheck, request.DocType);
            if (current.TryGetValue("formAmount", out var formAmount) && !string.IsNullOrWhiteSpace(formAmount))
                demoQ["formAmount"] = formAmount;
            var demoIssues = AiQuality.ValidateQuotation(demoQ).ToList();
            return Ok(demoQ, demoIssues, "demo", null, hash, demoIssues.Count == 0 ? "Quotation matches the form" : "Quotation flagged");
        }

        if (_llm != null && !string.IsNullOrWhiteSpace(documentText))
        {
            var json = _llm.CompleteJson(
                "Identify the actual CITF document kind. Return JSON only: detectedKind (nar1|br|quotation|other), readable (true/false), note. Do not copy the claimed slot if the text is a different document.",
                "Claimed slot: " + (request.DocType ?? schema.Key) + "\nFile name: " + (request.FileName ?? "") + "\nText sample:\n" + Truncate(documentText, 4000) + AiQuality.PortalLanguageInstruction(current));
            var docFields = AiJson.ParseObject(json);
            if (!docFields.ContainsKey("docType"))
                docFields["docType"] = request.DocType ?? schema.Key;
            docFields["expectedDocType"] = request.DocType ?? "";
            docFields["fileName"] = request.FileName ?? "";
            docFields["documentText"] = Truncate(documentText, 4000);
            if (string.IsNullOrWhiteSpace(docFields.GetValueOrDefault("detectedKind")))
                docFields["detectedKind"] = AiQuality.DetectDocKind(request.FileName, documentText);
            var docIssues = AiQuality.ValidateSupportingDoc(docFields).ToList();
            var notes = docIssues.Count > 0
                ? (docIssues[0].Message ?? docFields.GetValueOrDefault("note"))
                : (docFields.GetValueOrDefault("note") ?? "Readable");
            return Ok(docFields, docIssues, usedOcr ? "nova-ocr+llm" : LiveSource(), LiveModelId(), hash, notes);
        }

        if (!_config.AllowDemo)
            RequireLlmForMapping(documentText, hadFile);

        var demoDoc = AiDemoCatalog.Prefill(AiSchemaKeys.SupportingDoc, request.DocType);
        if (!demoDoc.ContainsKey("docType"))
            demoDoc["docType"] = request.DocType ?? schema.Key;
        demoDoc["expectedDocType"] = request.DocType ?? "";
        demoDoc["fileName"] = request.FileName ?? "";
        demoDoc["documentText"] = documentText ?? "";
        demoDoc["detectedKind"] = AiQuality.DetectDocKind(request.FileName, documentText);
        var supportingIssues = AiQuality.ValidateSupportingDoc(demoDoc).ToList();
        var demoNotes = supportingIssues.Count > 0
            ? supportingIssues[0].Message
            : demoDoc.GetValueOrDefault("note");
        return Ok(demoDoc, supportingIssues, "demo", null, hash, demoNotes);
    }

    AiAssistResponse ValidateForm(AiAssistRequest request, AiFormSchema schema, Dictionary<string, string> current, string hash)
    {
        if (schema.Key.Equals(AiSchemaKeys.DisbursementRequest, StringComparison.OrdinalIgnoreCase))
        {
            var drIssues = AiQuality.ValidateDisbursement(current).ToList();
            return Ok(current, drIssues, "rules", null, hash,
                drIssues.Count == 0 ? "AI automatic check passed for disbursement evidence" : "AI evidence issues found");
        }
        if (schema.Key.Equals(AiSchemaKeys.AnnualConfirmation, StringComparison.OrdinalIgnoreCase))
        {
            var annIssues = AiQuality.ValidateAnnualConfirmation(current).ToList();
            return Ok(current, annIssues, "rules", null, hash,
                annIssues.Count == 0 ? "AI automatic check passed for annual confirmation" : "AI quality issues found");
        }
        if (schema.Key.Equals(AiSchemaKeys.PalAnnualReply, StringComparison.OrdinalIgnoreCase))
        {
            return Ok(current, Array.Empty<AiIssue>(), "rules", null, hash,
                "AI automatic check: replies extracted and price vs P-rate verified");
        }

        if (_llm == null)
        {
            if (!_config.AllowDemo)
                throw new InvalidOperationException("D.Nova Chat is not configured (NOVA_API_KEY / ak- key).");
            var demoIssues = AiQuality.ValidateFundingApplication(current).ToList();
            return Ok(current, demoIssues, "rules", null, hash, demoIssues.Count == 0 ? "AI completeness check passed" : "AI quality issues found");
        }

        List<AiIssue> issues;
        var source = LiveSource();
        var model = LiveModelId();
        try
        {
            var json = _llm.CompleteJson(
                "Return JSON only with pass and issues array for CITF quality checking.",
                AiQuality.BuildFundingValidatePrompt(current, schema));
            var parsed = AiJson.ParseObject(json);
            issues = AiJson.ParseIssues(ExtractArray(json, "issues"));
            var setting = parsed.GetValueOrDefault("setting") ?? "";
            if (!string.IsNullOrWhiteSpace(setting)
                && !issues.Any(i => string.Equals(i.Code, "work_setting", StringComparison.OrdinalIgnoreCase)))
            {
                issues.Add(new AiIssue
                {
                    Field = "objective",
                    Code = "work_setting",
                    Severity = "warn",
                    Message = AiQuality.IsTraditional(current)
                        ? "人工智能判斷此項目目標屬" + (setting == "indoor" ? "室內" : setting == "outdoor" ? "室外" : setting == "mixed" ? "室內及室外" : "未註明室內或室外") + "工程。"
                        : "AI judges the project objective as " + setting + " work."
                });
            }
            if (issues.Count == 0 && parsed.GetValueOrDefault("pass") == "false")
                issues = AiQuality.ValidateFundingApplication(current).ToList();
        }
        catch when (_config.AllowDemo)
        {
            issues = AiQuality.ValidateFundingApplication(current).ToList();
            source = "rules-fallback";
            model = null;
        }

        issues = AiQuality.MergeFundingIssues(issues, current);

        return Ok(current, issues, source, model, hash, issues.Count == 0 ? "AI completeness check passed" : "AI quality issues found");
    }

    AiAssistResponse Suggest(AiAssistRequest request, Dictionary<string, string> current, string hash)
    {
        var q = (current.GetValueOrDefault("query") ?? request.DocumentText ?? "").ToLowerInvariant();
        if (_llm == null)
        {
            if (!_config.AllowDemo)
                throw new InvalidOperationException("D.Nova Chat is not configured (NOVA_API_KEY / ak- key).");
            return Ok(DemoSuggestions(q), Array.Empty<AiIssue>(), "demo", null, hash, "Demo suggestions");
        }

        try
        {
            var json = _llm.CompleteJson(
                "Suggest up to 3 CITF pre-approved product codes/names as JSON {suggestions:[{code,name}]}.",
                "User need: " + q + AiQuality.PortalLanguageInstruction(current));
            var suggestions = ExtractArray(json, "suggestions");
            var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["suggestionsJson"] = string.IsNullOrWhiteSpace(suggestions) || suggestions == "[]" ? "[]" : suggestions
            };
            return Ok(fields, Array.Empty<AiIssue>(), LiveSource(), LiveModelId(), hash, "AI suggestions");
        }
        catch when (_config.AllowDemo)
        {
            return Ok(DemoSuggestions(q), Array.Empty<AiIssue>(), "demo", null, hash, "Demo suggestions");
        }
    }

    AiAssistResponse Summarize(Dictionary<string, string> current, string hash)
    {
        if (_llm == null)
        {
            if (!_config.AllowDemo)
                throw new InvalidOperationException("D.Nova Chat is not configured (NOVA_API_KEY / ak- key).");
            return Ok(DemoSummary(current), Array.Empty<AiIssue>(), "demo", null, hash, DemoSummary(current).GetValueOrDefault("summary"));
        }

        try
        {
            var json = _llm.CompleteJson(
                string.Equals(current.GetValueOrDefault("listingKind"), "pal", StringComparison.OrdinalIgnoreCase)
                    ? "Return JSON only with summary, officerSummary, score and risks for a CITF PAL listing submission."
                    : "Return JSON only with summary, officerSummary, score and risks for a CITF funding submission. summary is short for the applicant; officerSummary is the 11-section Funding Application Analysis Template v2 report.",
                AiQuality.BuildSummaryPrompt(current));
            var parsed = AiJson.ParseObject(json);
            var risks = ExtractArray(json, "risks");
            var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["summary"] = parsed.GetValueOrDefault("summary") ?? "",
                ["officerSummary"] = parsed.GetValueOrDefault("officerSummary") ?? "",
                ["score"] = parsed.GetValueOrDefault("score") ?? "80",
                ["setting"] = parsed.GetValueOrDefault("setting") ?? "",
                ["risksJson"] = string.IsNullOrWhiteSpace(risks) ? "[]" : risks
            };
            SplitPalSummaries(fields, current);
            SplitFundingSummaries(fields, current);
            if (string.IsNullOrWhiteSpace(fields["summary"]))
                fields["summary"] = DemoSummary(current).GetValueOrDefault("summary") ?? "";
            if (!string.Equals(current.GetValueOrDefault("listingKind"), "pal", StringComparison.OrdinalIgnoreCase))
                AiQuality.ApplyWorkSetting(fields, current);
            return Ok(fields, Array.Empty<AiIssue>(), LiveSource(), LiveModelId(), hash, fields["summary"]);
        }
        catch when (_config.AllowDemo)
        {
            var demo = DemoSummary(current);
            return Ok(demo, Array.Empty<AiIssue>(), "demo", null, hash, demo.GetValueOrDefault("summary"));
        }
    }

    AiAssistResponse OcrBusyFallback(AiAssistRequest request, Dictionary<string, string> current, string hash, string message)
    {
        if (AiQuality.IsSignedApplicationForm(request.DocType))
        {
            var issues = AiQuality.ValidateSignedForm(request.FileName, request.DocumentText, current).ToList();
            if (issues.Count == 0 && !Regex.IsMatch(request.FileName ?? "", @"-signed(\.pdf)?$", RegexOptions.IgnoreCase))
                issues.Add(new AiIssue { Field = "readable", Code = "ocr_busy", Message = message, Severity = "error" });
            return Ok(current, issues, "ocr-busy", LiveModelId(), hash, issues.Count == 0
                ? "OCR busy; signed file name accepted. Click Replace later to re-read."
                : message);
        }
        var detected = AiQuality.DetectDocKind(request.FileName, request.DocumentText);
        var mismatch = AiQuality.DocSlotMismatch(request.DocType, detected);
        if (mismatch == null && !string.IsNullOrWhiteSpace(detected))
        {
            var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["readable"] = "true",
                ["fileName"] = request.FileName ?? "",
                ["detectedKind"] = detected,
                ["docType"] = request.DocType ?? ""
            };
            return Ok(fields, Array.Empty<AiIssue>(), "ocr-busy", LiveModelId(), hash,
                "OCR is busy (429). File name matches this slot; click Replace later to re-read the file.");
        }
        return Ok(new Dictionary<string, string>(), new[]
        {
            new AiIssue { Field = "readable", Code = "ocr_busy", Message = message, Severity = "error" }
        }, "ocr-busy", LiveModelId(), hash, message);
    }

    void RequireLlmForMapping(string documentText, bool hadFile)
    {
        if (_llm != null && !string.IsNullOrWhiteSpace(documentText))
            return;

        if (hadFile && _ocr == null)
            throw new InvalidOperationException("Document upload requires NOVA_OCR_API_KEY (sk-).");
        if (string.IsNullOrWhiteSpace(documentText))
            throw new InvalidOperationException("No document text. Upload a file, paste a product webpage URL, or provide documentText.");
        if (_llm == null)
            throw new InvalidOperationException("D.Nova Chat is not configured (NOVA_API_KEY / ak- key).");
    }

    string ResolveDocumentText(AiAssistRequest request, out bool usedOcr)
    {
        usedOcr = false;
        var documentText = request.DocumentText ?? "";
        if (!string.IsNullOrWhiteSpace(documentText)) return documentText;

        var pageUrl = PageUrl(request);
        if (!string.IsNullOrWhiteSpace(pageUrl))
        {
            if (_web == null)
            {
                if (_config.AllowDemo) return documentText;
                throw new InvalidOperationException("Webpage fetch is not available.");
            }
            return _web.FetchText(pageUrl!);
        }

        var fileBytes = Decode(request.FileBase64);
        if (fileBytes.Length == 0) return documentText;

        var embedded = PdfText.Extract(fileBytes);
        if (!string.IsNullOrWhiteSpace(embedded))
            return embedded;

        if (_ocr == null)
        {
            if (_config.AllowDemo) return documentText;
            throw new InvalidOperationException("Document upload requires NOVA_OCR_API_KEY (sk-).");
        }

        documentText = _ocr.ExtractText(fileBytes, request.FileName) ?? "";
        if (string.IsNullOrWhiteSpace(documentText))
            throw new InvalidOperationException("D.Nova OCR returned empty text for " + (request.FileName ?? "upload") + ".");
        usedOcr = true;
        return documentText;
    }

    static Dictionary<string, string> DemoSuggestions(string q)
    {
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (q.Contains("safety") || q.Contains("crane") || q.Contains("camera"))
            fields["suggestionsJson"] = "[{\"code\":\"PA-ACT-0412\",\"name\":\"SiteEye AI Safety Camera System (4S)\"},{\"code\":\"PA-ACT-0441\",\"name\":\"Smart Lifting Monitoring System (4S)\"}]";
        else if (q.Contains("drone") || q.Contains("survey"))
            fields["suggestionsJson"] = "[{\"code\":\"PA-ACT-0377\",\"name\":\"DroneScan Site Survey Kit\"}]";
        else if (q.Contains("bim"))
            fields["suggestionsJson"] = "[{\"code\":\"PA-BIM-0105\",\"name\":\"BIM Authoring Suite (1-year licence)\"}]";
        else
            fields["suggestionsJson"] = "[]";
        return fields;
    }

    static string? PageUrl(AiAssistRequest request)
    {
        var map = AiJson.ParseObject(request.CurrentValuesJson);
        var url = map.GetValueOrDefault("pageUrl") ?? map.GetValueOrDefault("url") ?? "";
        return string.IsNullOrWhiteSpace(url) ? null : url.Trim();
    }

    static void SplitPalSummaries(Dictionary<string, string> fields, IDictionary<string, string> current)
    {
        if (!string.Equals(current.GetValueOrDefault("listingKind"), "pal", StringComparison.OrdinalIgnoreCase))
            return;
        var demo = DemoSummary(current);
        var shortDemo = demo.GetValueOrDefault("summary") ?? "";
        var longDemo = demo.GetValueOrDefault("officerSummary") ?? "";
        var summary = fields.GetValueOrDefault("summary") ?? "";
        var officer = fields.GetValueOrDefault("officerSummary") ?? "";
        if (AiQuality.IsDetailedPalSummary(summary) && !AiQuality.IsDetailedPalSummary(officer))
        {
            officer = summary;
            summary = shortDemo;
        }
        if (AiQuality.IsDetailedPalSummary(summary) || string.IsNullOrWhiteSpace(summary))
            summary = shortDemo;
        if (!AiQuality.IsDetailedPalSummary(officer))
            officer = longDemo;
        fields["summary"] = AiQuality.SanitizeSummaryText(summary);
        fields["officerSummary"] = AiQuality.SanitizeSummaryText(officer);
    }

    static void SplitFundingSummaries(Dictionary<string, string> fields, IDictionary<string, string> current)
    {
        if (string.Equals(current.GetValueOrDefault("listingKind"), "pal", StringComparison.OrdinalIgnoreCase))
            return;
        var demo = DemoSummary(current);
        var shortDemo = demo.GetValueOrDefault("summary") ?? "";
        var longDemo = demo.GetValueOrDefault("officerSummary") ?? "";
        var summary = fields.GetValueOrDefault("summary") ?? "";
        var officer = fields.GetValueOrDefault("officerSummary") ?? "";
        if (AiQuality.IsDetailedFundingSummary(summary) && !AiQuality.IsDetailedFundingSummary(officer))
        {
            officer = summary;
            summary = shortDemo;
        }
        if (AiQuality.IsDetailedFundingSummary(summary) || string.IsNullOrWhiteSpace(summary))
            summary = shortDemo;
        if (!AiQuality.IsDetailedFundingSummary(officer))
            officer = longDemo;
        fields["summary"] = AiQuality.SanitizeSummaryText(summary);
        fields["officerSummary"] = AiQuality.SanitizeSummaryText(officer);
    }

    static Dictionary<string, string> DemoSummary(IDictionary<string, string> current)
    {
        if (string.Equals(current.GetValueOrDefault("listingKind"), "pal", StringComparison.OrdinalIgnoreCase))
        {
            var cat = current.GetValueOrDefault("category") ?? "PAL";
            var co = current.GetValueOrDefault("company") ?? "the vendor";
            var mode = current.GetValueOrDefault("mode") ?? "New listing";
            var palItems = current.GetValueOrDefault("itemsPlain");
            if (string.IsNullOrWhiteSpace(palItems))
                palItems = AiQuality.FormatItemsPlain(current.GetValueOrDefault("itemsJson") ?? "[]");
            var decls = current.GetValueOrDefault("declsPlain") ?? "";
            var files = current.GetValueOrDefault("filesPlain") ?? "";
            var comps = current.GetValueOrDefault("listedComparators") ?? "";
            var signer = current.GetValueOrDefault("signer") ?? "";
            var applicantSummary =
                "1. " + co + " requests a " + mode + " under " + cat + ".\n"
                + "2. Items: " + palItems + ".\n"
                + "3. Unit prices and uploaded files are taken from the submitted form.\n"
                + "4. Local development and declarations are as ticked on the form.\n"
                + "5. This summary does not recommend approval or rejection.";
            var palSummary =
                "1. Assessment summary: " + co + " requests a " + mode + " under " + cat + ". Submitted product details, merits and files are taken from the form. No CIC recommendation code was supplied. This summary does not recommend approval or rejection.\n"
                + "2. Nine PAL information topics: " + palItems + "\n"
                + "3. Category-specific assessment: Assessment uses only the submitted specification, functions, licence or course fields for " + cat + ". Site conditions, interoperability or learner counts are stated only if they appear on the form.\n"
                + "4. Declarations and compliance: " + (string.IsNullOrWhiteSpace(decls) ? "Declaration ticks are taken from the form." : decls)
                + (string.IsNullOrWhiteSpace(signer) ? "" : " Signed by " + signer.Trim() + ".")
                + " Ticks are not independent legal verification. Annual statutory confirmation is not applicable for a new listing.\n"
                + "5. Supporting documents: " + (string.IsNullOrWhiteSpace(files) ? "Uploaded types are taken from the item file list." : files) + "\n"
                + "6. Approved and market comparisons: " + (string.IsNullOrWhiteSpace(comps) || comps.Equals("none", StringComparison.OrdinalIgnoreCase)
                    ? "The applicant did not supply an approved PAL comparator or a market comparator on this form."
                    : "Same-category listed comparators: " + comps + ".") + "\n"
                + "7. Current and historical price assessment: Unit rates and any price history are taken from the submitted items. If history is not supplied, it is treated as not found. No exchange rate was provided.\n"
                + "8. Annual review: Not applicable because this application is a " + mode + ".\n"
                + "9. Change request: " + (mode.IndexOf("Change", StringComparison.OrdinalIgnoreCase) >= 0
                    ? "Describe the requested change using only the submitted application fields."
                    : "Not applicable because this application is a " + mode + ".") + "\n"
                + "10. Case summary: Review item identity, merits, files and job-reference quality from the form. No CIC action code is proposed.\n"
                + "11. Evidence, discrepancies and clarification: Check whether job contacts are specific, whether required files are present, and whether claimed merits and patents are evidenced in documents.";
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["summary"] = applicantSummary,
                ["officerSummary"] = palSummary,
                ["score"] = "86",
                ["risksJson"] = "[]"
            };
        }
        var project = current.GetValueOrDefault("project") ?? "the nominated site";
        var schemes = current.GetValueOrDefault("schemes") ?? "CITF";
        var objective = current.GetValueOrDefault("objective") ?? "";
        var items = current.GetValueOrDefault("itemsJson") ?? "[]";
        var amount = current.GetValueOrDefault("amount") ?? "";
        var itemsPlain = AiQuality.FormatItemsPlain(items);
        var summary = "1. Applicant submitted a " + schemes + " funding application for " + project + ".\n"
            + "2. Objective (" + AiQuality.WordCount(objective) + " words) describes the work and expected benefits.\n"
            + "3. Items: " + itemsPlain + "."
            + (string.IsNullOrWhiteSpace(amount) ? "" : "\n4. Amount applied HK$" + amount + ".");
        var filesPlain = current.GetValueOrDefault("filesPlain") ?? "";
        var otherFund = current.GetValueOrDefault("otherFund") ?? "";
        var applicant = current.GetValueOrDefault("company") ?? "the applicant";
        var officerSummary =
            "1. Funding assessment summary: " + applicant + " requested " + schemes + " funding for " + project + ". Item eligibility, quotations, timing and funding capacity are taken only from the form. Outstanding inputs are listed in section 11. This draft does not recommend approval or rejection.\n"
            + "2. Applicant eligibility and related entities: Company identity is " + applicant + ". BR status, NAR1, connected companies and prior funding are Not assessed unless they appear in Data. Eligibility is not inferred from the product name.\n"
            + "3. Application completeness and evidence: Uploaded types: " + (string.IsNullOrWhiteSpace(filesPlain) ? "not listed on this form" : filesPlain) + ". Missing disbursement documents are not treated as an application gap.\n"
            + "4. Item-by-item assessment: " + itemsPlain + " Pre-approved versus non-pre-approved treatment follows the submitted item type and PAL code only.\n"
            + "5. Quotations and price assessment: Quotation identity, rates and selection are taken from uploaded quotation files if present. If quotations are not supplied, they are Not found. No fixed quotation count is assumed.\n"
            + "6. Project use, implementation and timing: Objective and item plans describe intended use on " + project + ". " + (string.IsNullOrWhiteSpace(objective) ? "Objective text was not supplied." : "Objective length is " + AiQuality.WordCount(objective) + " words.") + " Commitment dates are Not found unless entered.\n"
            + "7. Item funding assessment: Requested cost is taken from submitted unit prices and quantities. Eligible versus excluded components, co-funding ratio and caps are Not assessed here because they must come from current CIC rules.\n"
            + "8. Application totals and funding ceiling: Total requested amount is " + (string.IsNullOrWhiteSpace(amount) ? "not entered" : ("HK$" + amount)) + ". Verified assessed amount, utilisation and remaining ceiling are Not assessed. Other public funding: " + (string.IsNullOrWhiteSpace(otherFund) ? "not stated" : otherFund) + ".\n"
            + "9. Risk, rule exceptions and escalation: No duplicate, connected-party or timing exception is recorded on this form unless listed in Data. No risk score or approval probability is assigned.\n"
            + "10. Funding case summary: " + applicant + " seeks " + schemes + " funding at " + project + " for " + itemsPlain + " Review quotations, PAL match and missing files before a decision. No CIC action code is proposed.\n"
            + "11. Evidence, discrepancies and clarification: Confirm that uploaded files match the typed items and prices, and that any non-pre-approved item has comparison evidence if required.";
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["summary"] = summary,
            ["officerSummary"] = officerSummary,
            ["score"] = "82",
            ["risksJson"] = "[]"
        };
        AiQuality.ApplyWorkSetting(fields, current);
        return fields;
    }

    static byte[] Decode(string? b64)
    {
        if (string.IsNullOrWhiteSpace(b64)) return Array.Empty<byte>();
        try
        {
            var comma = b64.IndexOf(',');
            var payload = comma >= 0 ? b64.Substring(comma + 1) : b64;
            return Convert.FromBase64String(payload);
        }
        catch { return Array.Empty<byte>(); }
    }

    static AiAssistResponse Ok(IDictionary<string, string> fields, IReadOnlyList<AiIssue> issues, string source, string? model, string hash, string? notes)
        => new()
        {
            Pass = issues.All(i => !string.Equals(i.Severity, "error", StringComparison.OrdinalIgnoreCase)),
            Source = source,
            Model = model,
            FieldsJson = AiJson.ToObjectJson(fields),
            IssuesJson = AiJson.ToArrayJson(issues),
            Notes = notes,
            ContentHash = hash
        };

    static string ExtractArray(string json, string name)
    {
        var match = System.Text.RegularExpressions.Regex.Match(json, "\"" + name + "\"\\s*:\\s*(\\[.*\\])", System.Text.RegularExpressions.RegexOptions.Singleline);
        return match.Success ? match.Groups[1].Value : "[]";
    }

    static string Truncate(string text, int max) => text.Length <= max ? text : text.Substring(0, max);
}
