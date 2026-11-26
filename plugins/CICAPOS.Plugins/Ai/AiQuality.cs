using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace CICAPOS.Plugins.Ai;

/// <summary>
/// Deterministic quality gates used when Azure OpenAI is unavailable, and as a pre-filter before/after the model.
/// Messages keep the portal "(AI quality check)" wording so Step 4 UX stays stable.
/// </summary>
public static class AiQuality
{
    public const int ObjectiveMinWords = 12;
    public const int ObjectiveMaxWords = 250;

    public static int WordCount(string? text)
        => Regex.Matches(text?.Trim() ?? "", @"\S+").Count;

    public static IReadOnlyList<AiIssue> ValidateFundingApplication(IDictionary<string, string> values)
    {
        var issues = new List<AiIssue>();
        var itemsJson = values.GetValueOrDefault("itemsJson") ?? "[]";
        var index = 0;
        foreach (Match item in Regex.Matches(itemsJson, "\\{[^{}]+\\}"))
        {
            index++;
            var map = AiJson.ParseObject(item.Value);
            var plan = map.GetValueOrDefault("plan") ?? "";
            if (string.IsNullOrWhiteSpace(plan))
                issues.Add(Issue($"item:{index}.plan", "plan_missing", $"Item {index}: implementation plan is missing"));
        }
        return issues;
    }

    public static IReadOnlyList<AiIssue> ValidateMappedFields(AiFormSchema schema, IDictionary<string, string> fields)
    {
        var issues = new List<AiIssue>();
        foreach (var field in schema.Fields.Where(f => f.Required))
        {
            if (string.IsNullOrWhiteSpace(fields.GetValueOrDefault(field.Key)))
                issues.Add(Issue(field.Key, "required", $"{field.Label} is missing after AI extraction"));
        }
        return issues;
    }

    public static IReadOnlyList<AiIssue> ValidateQuotation(IDictionary<string, string> fields)
    {
        var issues = new List<AiIssue>();
        if (!TryMoney(fields.GetValueOrDefault("amount"), out var quoteAmt))
        {
            issues.Add(Issue("amount", "unreadable", "Quotation amount could not be read (AI quality check)"));
            return issues;
        }
        if (!TryMoney(fields.GetValueOrDefault("formAmount"), out var formAmt))
        {
            issues.Add(Issue("formAmount", "missing", "Form amount is missing for quotation comparison"));
            return issues;
        }
        if (Math.Abs(quoteAmt - formAmt) >= 0.5m)
        {
            issues.Add(Issue("amount", "mismatch",
                $"Quotation shows HK${quoteAmt:N0}; form shows HK${formAmt:N0}", "error"));
        }
        return issues;
    }

    public static IReadOnlyList<AiIssue> ValidateSupportingDoc(IDictionary<string, string> fields)
    {
        var issues = new List<AiIssue>();
        var readable = (fields.GetValueOrDefault("readable") ?? "true").Equals("true", StringComparison.OrdinalIgnoreCase);
        if (!readable)
            issues.Add(Issue("readable", "blurred", fields.GetValueOrDefault("note") ?? "Image is blurred – text cannot be read. Please upload a clearer copy."));
        var slot = fields.GetValueOrDefault("expectedDocType") ?? fields.GetValueOrDefault("docType") ?? "";
        var detected = fields.GetValueOrDefault("detectedKind");
        if (string.IsNullOrWhiteSpace(detected))
            detected = DetectDocKind(fields.GetValueOrDefault("fileName"), fields.GetValueOrDefault("documentText"));
        var mismatch = DocSlotMismatch(slot, detected);
        if (mismatch != null) issues.Add(mismatch);
        return issues;
    }

    public static string DetectDocKind(string? fileName, string? text)
    {
        var name = fileName ?? "";
        if (Regex.IsMatch(name, @"nar1|annual.?return|周年", RegexOptions.IgnoreCase)) return "nar1";
        if (Regex.IsMatch(name, @"business.?reg|(^|[^a-z0-9])br([^a-z0-9]|$)|商业登记|商業登記", RegexOptions.IgnoreCase)) return "br";
        if (Regex.IsMatch(name, @"quotat|报价|報價", RegexOptions.IgnoreCase)) return "quotation";

        var body = text ?? "";
        if (string.IsNullOrWhiteSpace(body)) return "";
        if (Regex.IsMatch(body, @"NAR1|ANNUAL RETURN|share capital|company secretary", RegexOptions.IgnoreCase)) return "nar1";
        if (Regex.IsMatch(body, @"BUSINESS REGISTRATION CERTIFICATE|商业登记证|商業登記證", RegexOptions.IgnoreCase)) return "br";
        if (Regex.IsMatch(body, @"\bQUOTATION\b|unit price|total amount", RegexOptions.IgnoreCase)) return "quotation";
        return "";
    }

    public static string ExpectedDocKind(string? slot)
    {
        var s = slot ?? "";
        if (Regex.IsMatch(s, @"nar1|annual return", RegexOptions.IgnoreCase)) return "nar1";
        if (Regex.IsMatch(s, @"shareholder.?br|business registration|\bbr\b", RegexOptions.IgnoreCase)) return "br";
        if (Regex.IsMatch(s, @"quotation", RegexOptions.IgnoreCase)) return "quotation";
        return "";
    }

    public static AiIssue? DocSlotMismatch(string? slot, string? detectedKind)
    {
        var expected = ExpectedDocKind(slot);
        var detected = (detectedKind ?? "").Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(expected) || string.IsNullOrWhiteSpace(detected)) return null;
        if (string.Equals(expected, detected, StringComparison.OrdinalIgnoreCase)) return null;
        return Issue("docType", "wrong_type",
            "This file looks like a " + DocKindLabel(detected) + ", not " + (string.IsNullOrWhiteSpace(slot) ? DocKindLabel(expected) : slot)
            + ". Please swap or upload the correct document (AI quality check)");
    }

    static string DocKindLabel(string kind) => kind.ToLowerInvariant() switch
    {
        "nar1" => "NAR1 (Annual Return)",
        "br" => "Business Registration Certificate",
        "quotation" => "Quotation",
        _ => kind
    };

    public static bool IsSignedApplicationForm(string? docType)
        => Regex.IsMatch(docType ?? "", @"signed_application_form|signed and chopped", RegexOptions.IgnoreCase);

    public static IReadOnlyList<AiIssue> ValidateSignedForm(string? fileName, string? documentText, IDictionary<string, string> expected)
    {
        var issues = new List<AiIssue>();
        var blob = (fileName ?? "") + "\n" + (documentText ?? "");
        var unsignedSig = Regex.IsMatch(blob, @"\[\[UNSIGNED-SIGNATURE\]\]|UNSIGNED-SIGNATURE|Signature:\s*_{4,}", RegexOptions.IgnoreCase);
        var unsignedChop = Regex.IsMatch(blob, @"\[\[UNSIGNED-CHOP\]\]|UNSIGNED-CHOP|Company chop:\s*_{4,}", RegexOptions.IgnoreCase);
        var namedSigned = Regex.IsMatch(fileName ?? "", @"-signed(\.pdf)?$", RegexOptions.IgnoreCase);
        var signedEvidence = namedSigned || Regex.IsMatch(blob, @"\[\[SIGNED\]\]|signed by|has been signed|已签署|已簽署|electronic signature", RegexOptions.IgnoreCase);
        var chopEvidence = namedSigned || Regex.IsMatch(blob, @"\[\[CHOPPED\]\]|chop applied|company stamp|已盖章|已蓋章|公司印章", RegexOptions.IgnoreCase);
        if (unsignedSig || !signedEvidence)
            issues.Add(Issue("signature", "missing_signature", "No signature detected on the uploaded application form (AI quality check)"));
        if (unsignedChop || !chopEvidence)
            issues.Add(Issue("chop", "missing_chop", "No company chop detected on the uploaded application form (AI quality check)"));

        var refer = expected.GetValueOrDefault("ref") ?? "";
        if (!string.IsNullOrWhiteSpace(refer) && blob.IndexOf(refer, StringComparison.OrdinalIgnoreCase) < 0)
            issues.Add(Issue("form", "form_mismatch", "Uploaded file does not match the generated application form (reference " + refer + " missing) (AI quality check)"));
        var company = expected.GetValueOrDefault("company") ?? "";
        if (!string.IsNullOrWhiteSpace(company) && blob.IndexOf(company, StringComparison.OrdinalIgnoreCase) < 0)
            issues.Add(Issue("form", "form_mismatch", "Uploaded file does not match the generated application form (applicant name missing) (AI quality check)"));
        var grantDigits = Regex.Replace(expected.GetValueOrDefault("grant") ?? "", @"[^\d]", "");
        if (grantDigits.Length >= 4 && !Regex.IsMatch(blob, grantDigits))
            issues.Add(Issue("form", "form_mismatch", "Uploaded file does not match the generated application form (grant amount missing) (AI quality check)"));
        return issues;
    }

    public static IReadOnlyList<AiIssue> ValidateDisbursement(IDictionary<string, string> values)
    {
        var issues = new List<AiIssue>();
        var docsJson = values.GetValueOrDefault("docsJson") ?? "[]";
        if (string.IsNullOrWhiteSpace(docsJson) || docsJson == "[]")
            issues.Add(Issue("docs", "missing", "No evidence documents for AI check"));
        foreach (Match doc in Regex.Matches(docsJson, "\\{[^{}]+\\}"))
        {
            var map = AiJson.ParseObject(doc.Value);
            if (string.Equals(map.GetValueOrDefault("ai"), "flag", StringComparison.OrdinalIgnoreCase))
                issues.Add(Issue(map.GetValueOrDefault("type") ?? "doc", "flagged",
                    map.GetValueOrDefault("note") ?? "Evidence flagged by AI"));
        }
        if (!(values.GetValueOrDefault("paid") ?? "").Equals("true", StringComparison.OrdinalIgnoreCase)
            && !(values.GetValueOrDefault("paid") ?? "").Equals("True", StringComparison.OrdinalIgnoreCase)
            && values.GetValueOrDefault("paid") != "1")
        {
            // paid is enforced in the portal form; warn only when explicitly false
            if (string.Equals(values.GetValueOrDefault("paid"), "false", StringComparison.OrdinalIgnoreCase))
                issues.Add(Issue("paid", "not_paid", "Disbursement requires completed bank transfer (AI quality check)"));
        }
        return issues;
    }

    public static IReadOnlyList<AiIssue> ValidateAnnualConfirmation(IDictionary<string, string> values)
    {
        var issues = new List<AiIssue>();
        if (string.Equals(values.GetValueOrDefault("hasNc"), "Yes", StringComparison.OrdinalIgnoreCase)
            && string.IsNullOrWhiteSpace(values.GetValueOrDefault("ncJust")))
            issues.Add(Issue("ncJust", "missing", "Non-compliance justification is incomplete (AI quality check)"));
        foreach (var key in new[] { "prod", "safety", "quality" })
        {
            if (!int.TryParse(values.GetValueOrDefault(key), out var n) || n < 1 || n > 5)
                issues.Add(Issue(key, "range", $"Impact score {key} must be 1–5 (AI quality check)"));
        }
        return issues;
    }

    public static string BuildFundingValidatePrompt(IDictionary<string, string> values, AiFormSchema schema)
        => "You are a CITF application quality checker for the Construction Industry Council. "
            + "Return ONLY JSON: {\"pass\":boolean,\"issues\":[{\"field\":\"\",\"code\":\"\",\"message\":\"\",\"severity\":\"error|warn\"}]}. "
            + "Judge project text using language understanding only — never a local space/word-count rule. "
            + "If Data.schemes already lists any of ACT, BIM, STB, MiC, MiMEP, MPD, do not emit any issue on field schemes (not missing, not required). Do not require the objective to name the scheme code. "
            + "AI cameras, sensors and site safety monitoring belong to ACT. "
            + "If project (site name) is empty, add field project code missing. If objective is empty, add field objective code missing. "
            + "Emit mpdDate issues only when schemes includes MPD. otherFund value None/無 is valid. Ignore empty itemsJson on the project-objective step. "
            + "You judge whether the project objective is too short or too long. Do not count Chinese as one word because it has no spaces. "
            + "English: about " + ObjectiveMinWords + "–" + ObjectiveMaxWords + " words. Chinese: about 40–800 characters of real content. "
            + "If clearly too short, add issue code too_brief. If clearly too long, add issue code too_long. "
            + "The field must cover three parts: project objective, expected benefits, and a cost-effective implementation approach. "
            + "If a part is truly absent, use code weak_content. "
            + "Do not emit weak_content when the text already says what will be done, a quantified benefit (for example fewer accidents or fewer days), and how/where (install, deploy, floors, zones). Mixed indoor and outdoor work in one objective is valid. "
            + "Only the codes in Data.schemes are selected. ACT, BIM, STB, MiC, MiMEP, MPD in this instruction are vocabulary, not the applicant's ticked boxes. "
            + "Do not require the objective to mention any scheme that is not in Data.schemes. Do not say BIM/MiC/MiMEP/STB/MPD were selected unless they appear in Data.schemes. "
            + "Use your knowledge of what each selected scheme funds. Do not decide by a keyword list, and do not use the site name alone. "
            + "ACT funds advanced construction technology including cameras, sensors, smart site safety, and on-site construction robots (rebar-tying robots, welding robots, inspection robots). "
            + "STB funds prefabricated steel reinforcement cages produced off site, not on-site rebar-tying robots. "
            + "BIM funds building information modelling. MiC funds modular integrated construction. MiMEP funds multi-trade MEP modules. MPD funds manpower development and training. "
            + "If Data.schemes is only ACT and the work is ACT-eligible (including on-site robots), do not emit scheme_mismatch. "
            + "Never mention JSON keys, Data.schemes, or issue code names such as scheme_mismatch in messages. "
            + "If the written content clearly does not belong to a selected scheme, add issue code scheme_mismatch on field objective, and explain in message why it does not belong. "
            + "If it does belong, do not emit scheme_mismatch. "
            + "From the project objective and item plans, judge whether the work is indoor, outdoor, mixed or unspecified. Use construction knowledge of how the work is done. Do not use a keyword list. Do not treat the word 'site' or a contract/PWP number in the project title as proof of outdoor work. "
            + "Return top-level JSON key setting with that value. Also add issue code work_setting on field objective, severity warn, stating indoor or outdoor (or mixed) and the evidence from the objective. "
            + "Each item 'plan' must be a real implementation plan for that item on this project: how it will be installed, operated or used, where, and with at least one number (workers, floors, area, devices, days, savings). "
            + "Reject random typing, slogans, product names alone, or a short fragment that is only a benefit. Use plan_too_brief if too short, plan_weak if it is not actually a plan, plan_no_quantity if there is no number. "
            + "Do not emit plan_missing when that item already has a non-empty plan in itemsJson. "
            + "Do not emit too_brief for a substantial Chinese or English paragraph that already states what will be done, the benefit, and how. "
            + "Do not invent missing documents. Schema: " + schema.Title + ". "
            + PortalLanguageInstruction(values)
            + " Data: " + AiJson.ToObjectJson(values);

    public static List<AiIssue> MergeFundingIssues(IEnumerable<AiIssue>? model, IDictionary<string, string> values)
    {
        var hard = ValidateFundingApplication(values).ToList();
        var hardCodes = new HashSet<string>(hard.Select(h => h.Code), StringComparer.OrdinalIgnoreCase);
        var factual = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "plan_missing" };
        var itemsJson = values.GetValueOrDefault("itemsJson") ?? "[]";
        var plansFilled = true;
        var anyItem = false;
        foreach (Match item in Regex.Matches(itemsJson, "\\{[^{}]+\\}"))
        {
            anyItem = true;
            var plan = AiJson.ParseObject(item.Value).GetValueOrDefault("plan") ?? "";
            if (string.IsNullOrWhiteSpace(plan)) plansFilled = false;
        }
        if (!anyItem) plansFilled = false;

        var merged = new List<AiIssue>();
        foreach (var issue in model ?? Array.Empty<AiIssue>())
        {
            if (issue == null || (string.IsNullOrWhiteSpace(issue.Code) && string.IsNullOrWhiteSpace(issue.Message)))
                continue;
            if (IsHallucinatedFundingIssue(issue, hardCodes, factual, plansFilled, values))
                continue;
            if (merged.Any(x => x.Field == issue.Field && x.Code == issue.Code && x.Message == issue.Message))
                continue;
            merged.Add(issue);
        }
        foreach (var hardIssue in hard)
        {
            if (string.Equals(hardIssue.Field, "objective", StringComparison.OrdinalIgnoreCase))
                continue;
            if (!merged.Any(x => x.Field == hardIssue.Field && x.Code == hardIssue.Code))
                merged.Add(hardIssue);
        }
        return merged;
    }

    static bool IsHallucinatedFundingIssue(
        AiIssue issue,
        HashSet<string> hardCodes,
        HashSet<string> factual,
        bool plansFilled,
        IDictionary<string, string> values)
    {
        var code = issue.Code ?? "";
        var field = issue.Field ?? "";
        var schemes = values.GetValueOrDefault("schemes") ?? "";
        if (IsHallucinatedSchemeMismatch(issue, schemes))
            return true;
        if (string.Equals(code, "weak_content", StringComparison.OrdinalIgnoreCase)
            && ObjectiveAlreadyCoversBenefitAndHow(values.GetValueOrDefault("objective")))
            return true;
        if (string.Equals(code, "scheme_mismatch", StringComparison.OrdinalIgnoreCase)
            || string.Equals(code, "work_setting", StringComparison.OrdinalIgnoreCase)
            || string.Equals(code, "too_brief", StringComparison.OrdinalIgnoreCase)
            || string.Equals(code, "too_long", StringComparison.OrdinalIgnoreCase)
            || string.Equals(code, "weak_content", StringComparison.OrdinalIgnoreCase))
            return false;
        if (!string.IsNullOrWhiteSpace(schemes)
            && string.Equals(field, "schemes", StringComparison.OrdinalIgnoreCase)
            && Regex.IsMatch(code + " " + (issue.Message ?? ""), @"missing|required|empty|缺少", RegexOptions.IgnoreCase))
            return true;
        if (!Regex.IsMatch(schemes, @"\bMPD\b", RegexOptions.IgnoreCase)
            && string.Equals(field, "mpdDate", StringComparison.OrdinalIgnoreCase))
            return true;
        var blob = (code + " " + (issue.Message ?? "")).ToLowerInvariant();
        if (!string.IsNullOrWhiteSpace(code) && factual.Contains(code) && !hardCodes.Contains(code))
            return true;
        if (plansFilled && Regex.IsMatch(blob, @"plan_missing|plan is missing")
            && !Regex.IsMatch(blob, @"quantitative|too brief|plan_weak|plan_too_brief|数量|数字|过短|不是实施方案"))
            return true;
        return false;
    }

    static bool ObjectiveAlreadyCoversBenefitAndHow(string? objective)
    {
        var t = objective ?? "";
        if (t.Replace(" ", "").Length < 40) return false;
        var benefit = Regex.IsMatch(t, @"減少|降低|縮短|缩短|节省|節省|benefit|reduc|shorten|\bcut\b|improv|約\s*\d+|\d+\s*%|\d+\s*天");
        var how = Regex.IsMatch(t, @"安裝|部署|实施|實施|install|deploy|覆蓋|覆盖|層|层|區|区");
        return benefit && how;
    }

    static readonly string[] CitfSchemeCodes = { "MIMEP", "MIC", "MPD", "STB", "BIM", "ACT" };

    static HashSet<string> ParseSchemeCodes(string? raw)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var blob = (raw ?? "").ToUpperInvariant().Replace("MIMEP", "MIMEP");
        foreach (var code in CitfSchemeCodes)
        {
            if (Regex.IsMatch(blob, @"\b" + code + @"\b", RegexOptions.IgnoreCase))
                set.Add(code);
        }
        return set;
    }

    static bool IsHallucinatedSchemeMismatch(AiIssue issue, string schemes)
    {
        if (!string.Equals(issue.Code, "scheme_mismatch", StringComparison.OrdinalIgnoreCase))
            return false;
        var selected = ParseSchemeCodes(schemes);
        if (selected.Count == 0) return false;
        var msg = issue.Message ?? "";
        var mentioned = ParseSchemeCodes(msg);
        if (Regex.IsMatch(msg, @"Data\.schemes|scheme_mismatch|應發出|应发出", RegexOptions.IgnoreCase))
            return true;
        var extra = mentioned.Where(c => !selected.Contains(c)).ToList();
        if (extra.Count == 0) return false;
        if (Regex.IsMatch(msg, @"未有提及|未提及|未涵蓋|未涵盖|不涵蓋|不涵盖|does not (mention|cover|include)|not mention|已選計劃|已选计划|selected scheme", RegexOptions.IgnoreCase))
            return true;
        if (selected.Contains("ACT") && extra.Contains("STB")
            && Regex.IsMatch(msg, @"機器人|机器人|robot|綁扎|绑扎|自動化|自动化", RegexOptions.IgnoreCase))
            return true;
        return false;
    }

    public static string BuildSummaryPrompt(IDictionary<string, string> values)
    {
        var listing = values.GetValueOrDefault("listingKind") ?? "";
        if (listing.Equals("pal", StringComparison.OrdinalIgnoreCase))
        {
            return "Write a CITF Pre-approved List (PAL) listing application summary for CIC PAL officers. "
                + "Return ONLY JSON: {\"summary\":\"4-8 numbered lines (1. 2. 3.) each one short sentence covering category, vendor, each product (name/model/price), local development, functions and innovation, CITF merits, job references, declarations and files\",\"score\":integer 0-100,\"risks\":[{\"severity\":\"warn|error|tip\",\"text\":\"short flag\"}]}. "
                + "Put each point on its own line. Do not inline the word Warn in the summary; put flags only in risks. "
                + "Be factual from the data. Do not give recommendations, advice or next steps. Never copy JSON, square brackets or curly braces into the summary. Do not invent products, patents or prices. This is a vendor listing to the PAL, not a funding application — do not discuss indoor/outdoor site work or grant amounts. "
                + PortalLanguageInstruction(values)
                + " Data: " + AiJson.ToObjectJson(values);
        }
        var hasNpa = HasNpa(values);
        var data = new Dictionary<string, string>(values, StringComparer.OrdinalIgnoreCase);
        data["itemsPlain"] = FormatItemsPlain(values.GetValueOrDefault("itemsJson") ?? "[]");
        data["hasNpa"] = hasNpa ? "true" : "false";
        data.Remove("itemsJson");
        data.Remove("palJson");
        var palRule = hasNpa
            ? "Data.hasNpa is true. For each non-pre-approved item, compare it with the closest Pre-approved List product. State which unit price is more cost-effective, and whose functions better match this project's need (from the objective and the item plan). Start that line with 'Function and price comparison:'. Put the same text in risks with severity tip. Do not use warn for a PAL match. "
            : "Data.hasNpa is false. All items are pre-approved. Do not mention PAL comparison, alternatives, Function and price comparison, or close listed products. ";
        return "Write a CITF funding application submission summary for CIC officers. "
            + "Return ONLY JSON: {\"summary\":\"4-8 numbered lines (1. 2. 3.) each one short sentence covering scheme, site, objective, indoor/outdoor judgment, items and amounts\",\"score\":integer 0-100,\"setting\":\"indoor|outdoor|mixed|unspecified\",\"risks\":[{\"severity\":\"warn|error|tip\",\"text\":\"short flag\"}]}. Put each point on its own line. Do not inline the word Warn in the summary; put flags only in risks. "
            + "Do not give recommendations, advice or next steps. Be factual from the data. Do not invent documents or PAL codes. "
            + "Never copy JSON, square brackets, curly braces, itemsJson, palJson or field keys such as plan/product/paCode into the summary. Use Data.itemsPlain for item names in ordinary English. "
            + "Do not mention AI analysis, that analysis is complete or OK, or that a CIC officer still reviews the case. "
            + "Use severity warn only when the input is not detailed enough. "
            + "Judge indoor vs outdoor from the project objective and from item functions and implementation plans (how and where the equipment is used). "
            + "Never use the project title, contract number, PWP number, or the words Site / New Town / Extension alone as proof of outdoor work. "
            + "Write one numbered line: indoor, outdoor or mixed, with evidence from the objective or item functions/plans; then whether each item fits. If an item is indoor but the objective is outdoor (or the reverse), name the item. "
            + "Put that judgment in JSON key setting. "
            + "Add a warn risk only when the input is not detailed enough: setting is unspecified (no indoor/outdoor evidence), or the text does not cover the selected scheme/category criteria. "
            + palRule
            + PortalLanguageInstruction(values)
            + " Data: " + AiJson.ToObjectJson(data);
    }

    public static bool HasNpa(IDictionary<string, string>? values)
    {
        var itemsJson = values?.GetValueOrDefault("itemsJson") ?? "";
        foreach (Match item in Regex.Matches(itemsJson, "\\{[^{}]+\\}"))
        {
            var map = AiJson.ParseObject(item.Value);
            var type = map.GetValueOrDefault("type") ?? "";
            var paCode = map.GetValueOrDefault("paCode") ?? "";
            if (type.Equals("NPA", StringComparison.OrdinalIgnoreCase)
                || (string.IsNullOrWhiteSpace(paCode) && !type.Equals("PA", StringComparison.OrdinalIgnoreCase)))
                return true;
        }
        return false;
    }

    public static string FormatItemsPlain(string itemsJson)
    {
        var parts = new List<string>();
        foreach (Match item in Regex.Matches(itemsJson ?? "[]", "\\{[^{}]+\\}"))
        {
            var map = AiJson.ParseObject(item.Value);
            var name = (map.GetValueOrDefault("product") ?? map.GetValueOrDefault("name") ?? "item").Trim();
            if (string.IsNullOrWhiteSpace(name)) continue;
            var code = map.GetValueOrDefault("paCode") ?? "";
            var type = map.GetValueOrDefault("type") ?? "";
            var plan = (map.GetValueOrDefault("plan") ?? "").Trim();
            var kind = type.Equals("NPA", StringComparison.OrdinalIgnoreCase)
                || (string.IsNullOrWhiteSpace(code) && !type.Equals("PA", StringComparison.OrdinalIgnoreCase))
                ? "non-pre-approved" : "pre-approved " + (string.IsNullOrWhiteSpace(code) ? "item" : code);
            var line = name + " (" + kind + ")";
            if (!string.IsNullOrWhiteSpace(plan)) line += ": " + plan;
            parts.Add(line);
        }
        return parts.Count == 0 ? "No items listed" : string.Join("; ", parts);
    }

    public static string SanitizeSummaryText(string? text)
    {
        var s = text ?? "";
        s = Regex.Replace(s, @"\[\s*\{[\s\S]*?\}\s*\]", " ");
        s = Regex.Replace(s, @"\{[^{}]{6,}\}", " ");
        s = Regex.Replace(s, @"[\[\]{}]+", " ");
        s = Regex.Replace(s, @"""[A-Za-z][A-Za-z0-9_]*""\s*:", " ");
        var lines = s.Replace("\r\n", "\n").Split('\n');
        var kept = new List<string>();
        foreach (var raw in lines)
        {
            var line = Regex.Replace(raw, @"\s+", " ").Trim();
            line = Regex.Replace(line, @"^[\]\},;:\s]+", "").Trim();
            line = Regex.Replace(line, @"\s*Items:\s*$", "").Trim();
            line = Regex.Replace(line, @"\s*AI text analysis completed[^.。]*[.。]?", "", RegexOptions.IgnoreCase).Trim();
            line = Regex.Replace(line, @"\s*a CIC officer still reviews[^.。]*[.。]?", "", RegexOptions.IgnoreCase).Trim();
            if (line.Length < 8) continue;
            if (Regex.IsMatch(line, @"^[\[\{]|itemsJson|palJson|""paCode""")) continue;
            if (Regex.IsMatch(line, @"AI text analysis completed|officer still reviews|AI analysis (is |was )?(ok|complete)", RegexOptions.IgnoreCase)) continue;
            kept.Add(line);
        }
        return string.Join("\n", kept);
    }

    public static void ScrubPalCompareIfNoNpa(Dictionary<string, string> fields, IDictionary<string, string> values)
    {
        if (HasNpa(values)) return;
        var summary = SanitizeSummaryText(fields.GetValueOrDefault("summary"));
        var lines = summary.Replace("\r\n", "\n").Split('\n')
            .Where(l => !Regex.IsMatch(l, @"Function and price comparison|no alternative is needed|close listed product", RegexOptions.IgnoreCase));
        fields["summary"] = string.Join("\n", lines);
        var existing = fields.GetValueOrDefault("risksJson") ?? "[]";
        fields["risksJson"] = Regex.Replace(existing,
            @"\{[^{}]*(Function and price|close listed|no alternative is needed)[^{}]*\}(,)?",
            "", RegexOptions.IgnoreCase);
        fields["risksJson"] = Regex.Replace(fields["risksJson"], @"\[(\s*,)?\s*\]", "[]");
        fields["summary"] = SanitizeSummaryText(fields["summary"]);
    }

    public static bool IsTraditional(IDictionary<string, string>? values)
    {
        var lang = values?.GetValueOrDefault("lang") ?? "";
        return lang.Equals("TC", StringComparison.OrdinalIgnoreCase)
            || lang.Equals("zh-HK", StringComparison.OrdinalIgnoreCase)
            || lang.Equals("zh-Hant", StringComparison.OrdinalIgnoreCase);
    }

    public static string PortalLanguageInstruction(IDictionary<string, string>? values)
    {
        var lang = values?.GetValueOrDefault("lang") ?? "";
        if (IsTraditional(values))
            return " Portal language is Traditional Chinese (Hong Kong). Every issue.message, notes, summary and each risks text MUST be Traditional Chinese. Do not use Simplified Chinese. JSON keys and issue codes stay English. Extracted field values stay as printed on the document.";
        if (lang.Equals("SC", StringComparison.OrdinalIgnoreCase)
            || lang.Equals("zh-CN", StringComparison.OrdinalIgnoreCase)
            || lang.Equals("zh-Hans", StringComparison.OrdinalIgnoreCase))
            return " Portal language is Simplified Chinese. Every issue.message, notes, summary and each risks text MUST be Simplified Chinese. Do not use English sentences. JSON keys and issue codes stay English. Extracted field values stay as printed on the document.";
        return " Portal language is English. Every issue.message, notes, summary and each risks text MUST be English. JSON keys and issue codes stay English. Extracted field values stay as printed on the document.";
    }

    /// <summary>
    /// Indoor/outdoor is read from the objective and from item functions/plans (how the work is done),
    /// never from the project title.
    /// </summary>
    public static WorkSettingAssessment AssessWorkSetting(IDictionary<string, string> values)
    {
        var workText = values.GetValueOrDefault("objective") ?? "";
        var indoorEv = FirstCue(workText, IndoorProjectCue);
        var outdoorEv = FirstCue(workText, OutdoorProjectCue);
        var setting = indoorEv.Length > 0 && outdoorEv.Length > 0 ? "mixed"
            : indoorEv.Length > 0 ? "indoor"
            : outdoorEv.Length > 0 ? "outdoor"
            : "unspecified";

        var tc = IsTraditional(values);
        var risks = new List<string>();
        if (setting is "indoor" or "outdoor")
        {
            var index = 0;
            foreach (Match item in Regex.Matches(values.GetValueOrDefault("itemsJson") ?? "[]", "\\{[^{}]+\\}"))
            {
                index++;
                var map = AiJson.ParseObject(item.Value);
                var blob = (map.GetValueOrDefault("product") ?? "") + " " + (map.GetValueOrDefault("functions") ?? "")
                    + " " + (map.GetValueOrDefault("plan") ?? "");
                var itemIndoor = IndoorItemCue.IsMatch(blob);
                var itemOutdoor = OutdoorItemCue.IsMatch(blob);
                var place = itemIndoor && !itemOutdoor ? "indoor" : itemOutdoor && !itemIndoor ? "outdoor" : "";
                if (place.Length > 0 && !string.Equals(place, setting, StringComparison.OrdinalIgnoreCase))
                {
                    var name = map.GetValueOrDefault("product");
                    if (string.IsNullOrWhiteSpace(name)) name = tc ? "項目" : "item";
                    risks.Add(tc
                        ? "項目 " + index + " " + name.Trim() + " 屬於" + (place == "indoor" ? "室內" : "室外") + "設備，不符合" + (setting == "indoor" ? "室內" : "室外") + "需要"
                        : "Item " + index + " " + name.Trim() + " is " + place + " equipment and does not meet " + setting + " needs");
                }
            }
        }

        var evidence = setting == "indoor" ? indoorEv : outdoorEv;
        string sentence;
        if (tc && setting == "mixed")
            sentence = "工程同時涉及室內（" + indoorEv + "）和室外（" + outdoorEv + "）。請確認每一項物品對應相應部分。";
        else if (tc && setting == "unspecified")
            sentence = "項目目標和物品功能沒有說明是室內還是室外，因此未判斷物品是否符合室內或室外需要。";
        else if (tc && risks.Count > 0)
            sentence = "工程屬" + (setting == "indoor" ? "室內" : "室外") + "，依據目標及物品功能中的「" + evidence + "」判斷。所申請物品並非全部符合" + (setting == "indoor" ? "室內" : "室外") + "需要。";
        else if (tc)
            sentence = "工程屬" + (setting == "indoor" ? "室內" : "室外") + "，依據目標及物品功能中的「" + evidence + "」判斷。所申請物品符合" + (setting == "indoor" ? "室內" : "室外") + "使用需要。";
        else if (setting == "mixed")
            sentence = "Project setting is mixed: the objective mentions both indoor (" + indoorEv
                + ") and outdoor (" + outdoorEv + ") work. Confirm each item is for the matching part.";
        else if (setting == "unspecified")
            sentence = "Project setting is unspecified: the objective and item functions do not say whether the work is indoor or outdoor, so item suitability was not judged.";
        else if (risks.Count > 0)
            sentence = "Project setting is " + setting + ", judged from the objective and item functions (" + evidence
                + "). Requested items do not all suit " + setting + " use.";
        else
            sentence = "Project setting is " + setting + ", judged from the objective and item functions (" + evidence
                + "). Requested items suit " + setting + " use.";

        return new WorkSettingAssessment
        {
            Setting = setting,
            SummarySentence = sentence,
            Risks = risks
        };
    }

    public static void ApplyWorkSetting(Dictionary<string, string> fields, IDictionary<string, string> values)
    {
        if (string.Equals(values.GetValueOrDefault("listingKind"), "pal", StringComparison.OrdinalIgnoreCase))
            return;
        var assessment = AssessWorkSetting(values);
        fields["setting"] = assessment.Setting;
        fields["summary"] = StripSettingLines(fields.GetValueOrDefault("summary"));
        EnsureItemSettingLine(fields, assessment);
        ApplyPalCompare(fields, values);
        fields["summary"] = SanitizeSummaryText(fields.GetValueOrDefault("summary"));
        ScrubPalCompareIfNoNpa(fields, values);
        // Indoor/outdoor fit belongs in the summary only. Warn only when setting is unspecified.
        var existing = fields.GetValueOrDefault("risksJson");
        if (string.IsNullOrWhiteSpace(existing)) existing = "[]";
        if (assessment.Setting != "unspecified") return;
        if (existing.IndexOf("unspecified", StringComparison.OrdinalIgnoreCase) >= 0
            || existing.IndexOf("does not say whether", StringComparison.OrdinalIgnoreCase) >= 0)
            return;
        var extra = "{\"severity\":\"warn\",\"text\":" + JsonString(assessment.SummarySentence) + "}";
        fields["risksJson"] = existing.Trim() == "[]"
            ? "[" + extra + "]"
            : existing.Trim().TrimEnd(']') + "," + extra + "]";
    }

    static string SettingZh(string setting)
        => setting == "indoor" ? "室內" : setting == "outdoor" ? "室外" : setting == "mixed" ? "室內及室外" : "未註明室內或室外";

    static string StripSettingLines(string? text)
    {
        var kept = new List<string>();
        foreach (var raw in (text ?? "").Replace("\r\n", "\n").Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;
            if (Regex.IsMatch(line, @"work setting is|Project setting is|工程屬|工程属|Requested items (suit|do not all suit)|does not meet indoor|does not meet outdoor|不符合室內需要|不符合室外需要", RegexOptions.IgnoreCase))
                continue;
            kept.Add(line);
        }
        return string.Join("\n", kept);
    }

    static void EnsureItemSettingLine(Dictionary<string, string> fields, WorkSettingAssessment assessment)
    {
        var summary = StripSettingLines(fields.GetValueOrDefault("summary"));
        if (!Regex.IsMatch(summary, @"suit .+ use|does not all suit|Items vs objective|不符合.+需要|符合.+使用需要", RegexOptions.IgnoreCase))
        {
            summary = summary.Trim();
            if (summary.Length > 0)
            {
                var last = summary[summary.Length - 1];
                if (last != '.' && last != '。' && last != '\n') summary += ".";
            }
            fields["summary"] = (summary.Length == 0 ? "" : summary + "\n") + assessment.SummarySentence;
            summary = fields["summary"];
        }
        foreach (var itemLine in assessment.Risks)
        {
            if (string.IsNullOrWhiteSpace(itemLine)) continue;
            if (summary.IndexOf(itemLine, StringComparison.OrdinalIgnoreCase) >= 0) continue;
            summary = summary.Trim();
            if (summary.Length > 0)
            {
                var last = summary[summary.Length - 1];
                if (last != '.' && last != '。' && last != '\n') summary += ".";
            }
            var suffix = itemLine.EndsWith(".", StringComparison.Ordinal) || itemLine.EndsWith("。", StringComparison.Ordinal) ? "" : ".";
            summary = summary + "\n" + itemLine + suffix;
            fields["summary"] = summary;
        }
    }

    public static void ApplyPalCompare(Dictionary<string, string> fields, IDictionary<string, string> values)
    {
        var pal = values.GetValueOrDefault("palJson") ?? "[]";
        var itemsJson = values.GetValueOrDefault("itemsJson") ?? "[]";
        var need = (values.GetValueOrDefault("objective") ?? "") + " " + (values.GetValueOrDefault("project") ?? "");
        var tips = new List<string>();
        var anyNpa = false;
        foreach (Match item in Regex.Matches(itemsJson, "\\{[^{}]+\\}"))
        {
            var map = AiJson.ParseObject(item.Value);
            var type = map.GetValueOrDefault("type") ?? "";
            var paCode = map.GetValueOrDefault("paCode") ?? "";
            var product = (map.GetValueOrDefault("product") ?? "").Trim();
            if (string.IsNullOrWhiteSpace(product)) continue;
            var isNpa = type.Equals("NPA", StringComparison.OrdinalIgnoreCase)
                || (string.IsNullOrWhiteSpace(paCode) && !type.Equals("PA", StringComparison.OrdinalIgnoreCase));
            if (!isNpa) continue;
            anyNpa = true;
            var npaFn = map.GetValueOrDefault("functions") ?? "";
            var npaRaw = map.GetValueOrDefault("unit") ?? map.GetValueOrDefault("price") ?? "";
            var npaPx = HkMoney(npaRaw);
            var plan = map.GetValueOrDefault("plan") ?? "";
            var hit = FindSimilarPal(product, npaFn, pal);
            if (hit != null)
            {
                var code = hit.GetValueOrDefault("code") ?? "";
                var name = hit.GetValueOrDefault("name") ?? "";
                var listed = (string.IsNullOrWhiteSpace(code) ? "" : code + " ") + name;
                var palRaw = hit.GetValueOrDefault("price") ?? "";
                var palPx = HkMoney(palRaw);
                var palFn = hit.GetValueOrDefault("functions") ?? "";
                tips.Add("Function and price comparison: NPA “" + product + "” (" + npaPx + "; " + ClipText(npaFn, 80)
                    + ") vs listed " + listed.Trim() + " (" + palPx + "; " + ClipText(palFn, 80) + "). "
                    + PriceWinner(product, npaRaw, listed.Trim(), palRaw) + " "
                    + FunctionWinner(product, npaFn + " " + plan, listed.Trim(), palFn, need));
            }
            else
                tips.Add("Function and price comparison: NPA “" + product + "” (" + npaPx + "; " + ClipText(npaFn, 80)
                    + ") — no close listed product was found by function, so a price and function comparison against the Pre-approved List could not be made.");
        }
        if (!anyNpa || tips.Count == 0) return;

        var summary = fields.GetValueOrDefault("summary") ?? "";
        var keptLines = summary.Replace("\r\n", "\n").Split('\n')
            .Where(l => !Regex.IsMatch(l, @"Function and price comparison|compared by function", RegexOptions.IgnoreCase));
        summary = string.Join("\n", keptLines).Trim();
        if (summary.Length > 0 && summary[summary.Length - 1] != '.' && summary[summary.Length - 1] != '。'
            && summary[summary.Length - 1] != '\n')
            summary += ".";
        fields["summary"] = (summary.Length == 0 ? "" : summary + "\n") + string.Join("\n", tips);

        var existing = fields.GetValueOrDefault("risksJson") ?? "[]";
        existing = Regex.Replace(existing,
            @"\{[^{}]*(Function and price|close listed|no alternative is needed)[^{}]*\}(,)?",
            "", RegexOptions.IgnoreCase);
        existing = Regex.Replace(existing, @"\[(\s*,)?\s*\]", "[]");
        var extra = string.Join(",", tips.Select(t =>
            "{\"severity\":\"tip\",\"text\":" + JsonString(t) + "}"));
        fields["risksJson"] = existing.Trim() == "[]"
            ? "[" + extra + "]"
            : existing.Trim().TrimEnd(']') + "," + extra + "]";
    }

    static Dictionary<string, string>? FindSimilarPal(string product, string functions, string palJson)
    {
        var tokens = new List<string>();
        foreach (Match m in Regex.Matches((product + " " + functions).ToLowerInvariant(), @"[a-z0-9\u4e00-\u9fff]{4,}"))
        {
            if (m.Success && !tokens.Contains(m.Value)) tokens.Add(m.Value);
        }
        if (tokens.Count == 0) return null;
        Dictionary<string, string>? best = null;
        var bestN = 0;
        foreach (Match row in Regex.Matches(palJson ?? "[]", "\\{[^{}]+\\}"))
        {
            var map = AiJson.ParseObject(row.Value);
            var blob = ((map.GetValueOrDefault("code") ?? "") + " " + (map.GetValueOrDefault("name") ?? "")
                + " " + (map.GetValueOrDefault("functions") ?? "")).ToLowerInvariant();
            var n = tokens.Count(t => blob.Contains(t));
            if (n > bestN && n >= Math.Min(2, tokens.Count)) { bestN = n; best = map; }
        }
        return best;
    }

    static string PriceWinner(string npaName, string npaRaw, string listedName, string palRaw)
    {
        var npa = TryMoney(npaRaw);
        var pal = TryMoney(palRaw);
        if (npa == null || pal == null)
            return "Unit prices could not both be compared.";
        if (npa < pal)
            return "NPA “" + npaName + "” is more cost-effective (" + HkMoney(npaRaw) + " vs listed " + HkMoney(palRaw) + ").";
        if (pal < npa)
            return "Listed " + listedName + " is more cost-effective (" + HkMoney(palRaw) + " vs NPA " + HkMoney(npaRaw) + ").";
        return "Unit prices are similar (" + HkMoney(npaRaw) + ").";
    }

    static string FunctionWinner(string npaName, string npaFn, string listedName, string palFn, string need)
    {
        var npaScore = NeedOverlap(npaFn, need);
        var palScore = NeedOverlap(palFn, need);
        if (palScore > npaScore)
            return "Listed " + listedName + " functions better match this project’s need.";
        if (npaScore > palScore)
            return "NPA “" + npaName + "” functions better match this project’s need.";
        return "Functions are similar for this project’s need.";
    }

    static int NeedOverlap(string text, string need)
    {
        var hay = (need ?? "").ToLowerInvariant();
        if (hay.Length < 4) hay = (text ?? "").ToLowerInvariant();
        var n = 0;
        foreach (Match m in Regex.Matches((text ?? "").ToLowerInvariant(), @"[a-z0-9\u4e00-\u9fff]{4,}"))
        {
            if (hay.Contains(m.Value)) n++;
        }
        return n;
    }

    static decimal? TryMoney(string raw)
    {
        var digits = Regex.Replace(raw ?? "", @"[^\d.]", "");
        if (decimal.TryParse(digits, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var n) && n > 0)
            return n;
        return null;
    }

    static string HkMoney(string raw)
    {
        if (decimal.TryParse(raw, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var n) && n > 0)
            return "HK$" + n.ToString("0", System.Globalization.CultureInfo.InvariantCulture);
        return "price not stated";
    }

    static string ClipText(string value, int max)
    {
        var s = Regex.Replace(value ?? "", @"\s+", " ").Trim();
        if (s.Length == 0) return "functions not stated";
        return s.Length <= max ? s : s.Substring(0, max - 1) + "…";
    }

    static string JsonString(string value)
        => "\"" + (value ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

    public static string BuildPrefillPrompt(string documentText, AiFormSchema schema)
    {
        var extra = "";
        if (string.Equals(schema.Key, AiSchemaKeys.PalProduct, StringComparison.OrdinalIgnoreCase)
            || string.Equals(schema.Key, AiSchemaKeys.PalCourse, StringComparison.OrdinalIgnoreCase))
        {
            extra = " Always fill merits. obj must list at least one of: Productivity, Safety, Quality, Environmental performance, Cost-effectiveness, Industry capability (manpower). "
                + "jobsJson must be a JSON array with at least one {project,client,year,contact} taken from the document (job references / project sites). ";
        }
        return "Extract fields for a CITF portal form from the document text. "
            + "Return ONLY JSON object with these keys: "
            + string.Join(", ", schema.Fields.Select(f => f.Key))
            + ". Use empty string when unknown. Dates as YYYY-MM-DD. Numbers without currency symbols. "
            + extra
            + "Document:\n" + Truncate(documentText, 12000);
    }

    static bool HasObjectiveCue(string text)
        => Regex.IsMatch(text,
            @"aim|objective|goal|purpose|propos|intend|seek|will |install|deploy|adopt|introduc|apply for|\bcut\b|automat|monitor|improv|reduc|cover |目标|旨在|为了|拟在|申请",
            RegexOptions.IgnoreCase);

    static bool HasBenefitCue(string text)
        => Regex.IsMatch(text,
            @"benefit|improv|reduc|sav|safer|safety|productiv|efficien|cut |lower|enhance|quality|incident|cost-?effect|效益|节省|降低|减少|提升|安全",
            RegexOptions.IgnoreCase);

    static bool HasImplementCue(string text)
        => Regex.IsMatch(text,
            @"implement|deploy|install|monitor|train|use |using|cover|apply|roll.?out|operate|site|automat|tying|zone|crane|camera|survey|licence|license|实施|安装|部署|监测|培训",
            RegexOptions.IgnoreCase);

    public const int PlanMinWords = 8;
    public const int PlanMinChineseChars = 16;

    public static bool PlanHasEnoughDetail(string? text)
    {
        var t = text?.Trim() ?? "";
        if (t.Length == 0) return false;
        var cjk = Regex.Matches(t, @"[\u4e00-\u9fff]").Count;
        var western = Regex.Matches(Regex.Replace(t, @"[\u4e00-\u9fff]", " "), @"\S+").Count;
        return western >= PlanMinWords || cjk >= PlanMinChineseChars;
    }

    static bool HasPlanActionCue(string text)
        => Regex.IsMatch(text,
            @"\b(install|deploy|monitor|cover|alert|train|automat|operat|implement|apply|using|use |survey|tying|roll.?out)\b|安装|部署|监测|用于|使用|覆盖|培训|绑扎|巡查|在本工程|在地盘|在工地",
            RegexOptions.IgnoreCase);

    static readonly Regex IndoorProjectCue = new(
        @"indoor|interior|\bfit-?out\b|fitout|plant[- ]?room|basement|\blobby\b|office renovation|办公室装修|辦公室裝修|室内|室內|内装|內裝|机房|機房|课室|課室",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    static readonly Regex OutdoorProjectCue = new(
        @"outdoor|open[- ]air|tower[- ]?crane|excavation|site formation|foundation works|reclamation|viaduct|sports park|open site|civil works|室外|戶外|户外|天秤|塔吊|地基|外墙|外牆",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    static readonly Regex IndoorItemCue = new(
        @"\bindoor\b|plant[- ]?room|mep module|mep cassette|integrated riser|indoor air|interior finish|office fit-?out|hvac|\bahu\b|classroom|lobby|室内装修|室內裝修|机房|機房|室内|室內",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    static readonly Regex OutdoorItemCue = new(
        @"\bdrone|\buav\b|unmanned aerial|tower[- ]?crane|lifting monitor|site survey|\boutdoor\b|podium|excavation|facade|外墙|外牆|无人机|無人機",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    static string FirstCue(string text, Regex pattern)
    {
        var match = pattern.Match(text ?? "");
        return match.Success ? Regex.Replace(match.Value, @"\s+", " ").Trim() : "";
    }

    static bool TryMoney(string? raw, out decimal value)
        => decimal.TryParse(Regex.Replace(raw ?? "", @"[^\d.]", ""), NumberStyles.Number, CultureInfo.InvariantCulture, out value);

    static AiIssue Issue(string field, string code, string message, string severity = "error")
        => new() { Field = field, Code = code, Message = message, Severity = severity };

    static string Truncate(string text, int max)
        => string.IsNullOrEmpty(text) || text.Length <= max ? text ?? "" : text.Substring(0, max);
}

public sealed class WorkSettingAssessment
{
    public string Setting { get; set; } = "unspecified";
    public string SummarySentence { get; set; } = "";
    public IReadOnlyList<string> Risks { get; set; } = Array.Empty<string>();
}
