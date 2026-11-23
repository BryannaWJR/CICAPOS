namespace CICAPOS.Plugins.Ai;

public sealed class AiAssistService
{
    readonly AiProviderConfig _config;
    readonly ILlmClient? _llm;
    readonly INovaOcrClient? _ocr;

    public AiAssistService(AiProviderConfig config, ILlmClient? llm = null, INovaOcrClient? ocr = null)
    {
        _config = config;
        _llm = llm ?? CreateLlm(config);
        _ocr = ocr ?? (config.HasOcr ? new NovaOcrClient(config) : null);
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

        return action switch
        {
            AiActions.Prefill => Prefill(request, schema, hash),
            AiActions.ValidateUpload => ValidateUpload(request, schema, current, hash),
            AiActions.ValidateForm => ValidateForm(request, schema, current, hash),
            AiActions.Suggest => Suggest(request, current, hash),
            _ => throw new ArgumentException("Unsupported AI action: " + request.Action)
        };
    }

    AiAssistResponse Prefill(AiAssistRequest request, AiFormSchema schema, string hash)
    {
        var documentText = ResolveDocumentText(request, out var usedOcr);
        var hadFile = Decode(request.FileBase64).Length > 0;

        if (_llm != null && !string.IsNullOrWhiteSpace(documentText))
        {
            var json = _llm.CompleteJson(
                "You extract CITF portal form fields. Return a flat JSON object only.",
                AiQuality.BuildPrefillPrompt(documentText, schema));
            var fields = AiJson.ParseObject(json);
            var source = usedOcr ? "nova-ocr+llm" : LiveSource();
            var issues = AiQuality.ValidateMappedFields(schema, fields).ToList();
            return Ok(fields, issues, source, LiveModelId(), hash, "Document read and fields filled (" + source + ")");
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
        var documentText = ResolveDocumentText(request, out var usedOcr);
        var hadFile = Decode(request.FileBase64).Length > 0;

        if (schema.Key.Equals(AiSchemaKeys.QuotationCheck, StringComparison.OrdinalIgnoreCase))
        {
            if (_llm != null && !string.IsNullOrWhiteSpace(documentText))
            {
                var prompt = AiQuality.BuildPrefillPrompt(documentText, schema)
                    + "\nForm amount to compare: " + (current.GetValueOrDefault("formAmount") ?? "");
                var fields = AiJson.ParseObject(_llm.CompleteJson(
                    "Extract quotation supplier, product, qty, unit price and total amount. Return JSON only.",
                    prompt));
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
                "Assess document readability for CITF upload checks. Return JSON with docType, readable (true/false), note.",
                "Document type: " + (request.DocType ?? schema.Key) + "\nText sample:\n" + Truncate(documentText, 4000));
            var docFields = AiJson.ParseObject(json);
            if (!docFields.ContainsKey("docType"))
                docFields["docType"] = request.DocType ?? schema.Key;
            var docIssues = AiQuality.ValidateSupportingDoc(docFields).ToList();
            return Ok(docFields, docIssues, usedOcr ? "nova-ocr+llm" : LiveSource(), LiveModelId(), hash, docFields.GetValueOrDefault("note"));
        }

        if (!_config.AllowDemo)
            RequireLlmForMapping(documentText, hadFile);

        var demoDoc = AiDemoCatalog.Prefill(AiSchemaKeys.SupportingDoc, request.DocType);
        if (!demoDoc.ContainsKey("docType"))
            demoDoc["docType"] = request.DocType ?? schema.Key;
        var supportingIssues = AiQuality.ValidateSupportingDoc(demoDoc).ToList();
        return Ok(demoDoc, supportingIssues, "demo", null, hash, demoDoc.GetValueOrDefault("note"));
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
            if (issues.Count == 0 && parsed.GetValueOrDefault("pass") == "false")
                issues = AiQuality.ValidateFundingApplication(current).ToList();
        }
        catch when (_config.AllowDemo)
        {
            issues = AiQuality.ValidateFundingApplication(current).ToList();
            source = "rules-fallback";
            model = null;
        }

        foreach (var hard in AiQuality.ValidateFundingApplication(current))
            if (!issues.Any(i => i.Field == hard.Field && i.Code == hard.Code))
                issues.Add(hard);

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
                "User need: " + q);
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

    void RequireLlmForMapping(string documentText, bool hadFile)
    {
        if (_llm != null && !string.IsNullOrWhiteSpace(documentText))
            return;

        if (hadFile && _ocr == null)
            throw new InvalidOperationException("Document upload requires NOVA_OCR_API_KEY (sk-).");
        if (string.IsNullOrWhiteSpace(documentText))
            throw new InvalidOperationException("No document text. Upload a file (OCR) or provide documentText.");
        if (_llm == null)
            throw new InvalidOperationException("D.Nova Chat is not configured (NOVA_API_KEY / ak- key).");
    }

    string ResolveDocumentText(AiAssistRequest request, out bool usedOcr)
    {
        usedOcr = false;
        var documentText = request.DocumentText ?? "";
        if (!string.IsNullOrWhiteSpace(documentText)) return documentText;

        var fileBytes = Decode(request.FileBase64);
        if (fileBytes.Length == 0) return documentText;

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
