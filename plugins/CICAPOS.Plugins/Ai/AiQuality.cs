using System.Globalization;
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
        var objective = values.GetValueOrDefault("objective") ?? "";
        if (string.IsNullOrWhiteSpace(objective))
        {
            issues.Add(Issue("objective", "missing", "Project objective and expected benefits are missing"));
        }
        else
        {
            var words = WordCount(objective);
            if (words > ObjectiveMaxWords)
                issues.Add(Issue("objective", "too_long",
                    "Project objective is over 250 words (now " + words + ") – AI quality check"));
            else if (words < ObjectiveMinWords)
                issues.Add(Issue("objective", "too_brief",
                    "Project objective is too brief (minimum 12 words, now " + words + ") – describe benefits and implementation (AI quality check)"));
            else if (!HasBenefitCue(objective) || !HasImplementCue(objective))
                issues.Add(Issue("objective", "weak_content", "Project objective should cover expected benefits and how you will implement (AI quality check)"));
        }

        var itemsJson = values.GetValueOrDefault("itemsJson") ?? "[]";
        var index = 0;
        foreach (Match item in Regex.Matches(itemsJson, "\\{[^{}]+\\}"))
        {
            index++;
            var map = AiJson.ParseObject(item.Value);
            var plan = map.GetValueOrDefault("plan") ?? "";
            if (string.IsNullOrWhiteSpace(plan))
                issues.Add(Issue($"item:{index}.plan", "plan_missing", $"Item {index}: implementation plan is missing"));
            else if (!Regex.IsMatch(plan, @"\d"))
                issues.Add(Issue($"item:{index}.plan", "plan_no_quantity", $"Item {index}: implementation plan has no quantitative detail (e.g. number of workers, floors, area or expected savings) – AI quality check"));
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
            + "Count words in the project objective (whitespace-separated tokens). "
            + "If fewer than " + ObjectiveMinWords + " words, add issue code too_brief. "
            + "If more than " + ObjectiveMaxWords + " words, add issue code too_long. "
            + "The filled objective MUST sit inside that min/max range. "
            + "Also check that the objective describes expected benefits AND how it will be implemented (not just a title). "
            + "Each item implementation plan needs at least one quantitative detail (workers, floors, area, savings, devices, days). "
            + "Do not invent missing documents. Schema: " + schema.Title + ". "
            + "Data: " + AiJson.ToObjectJson(values);

    public static string BuildSummaryPrompt(IDictionary<string, string> values)
        => "Write a CITF funding application submission summary for CIC officers. "
            + "Return ONLY JSON: {\"summary\":\"120-180 words covering scheme, site, objective, items, amounts and any risks\",\"score\":integer 0-100,\"risks\":[\"short flag\"]}. "
            + "Be factual from the data. Do not invent documents. Data: " + AiJson.ToObjectJson(values);

    public static string BuildPrefillPrompt(string documentText, AiFormSchema schema)
        => "Extract fields for a CITF portal form from the document text. "
            + "Return ONLY JSON object with these keys: "
            + string.Join(", ", schema.Fields.Select(f => f.Key))
            + ". Use empty string when unknown. Dates as YYYY-MM-DD. Numbers without currency symbols. "
            + "Document:\n" + Truncate(documentText, 12000);

    static bool HasBenefitCue(string text)
        => Regex.IsMatch(text, @"benefit|improv|reduc|sav|safer|safety|productiv|efficien|cut |lower|enhance|quality|incident", RegexOptions.IgnoreCase);

    static bool HasImplementCue(string text)
        => Regex.IsMatch(text, @"implement|deploy|install|monitor|train|use |using|cover|apply|roll.?out|operate|site|automat|tying|zone|crane|camera|survey|licence|license", RegexOptions.IgnoreCase);

    static bool TryMoney(string? raw, out decimal value)
        => decimal.TryParse(Regex.Replace(raw ?? "", @"[^\d.]", ""), NumberStyles.Number, CultureInfo.InvariantCulture, out value);

    static AiIssue Issue(string field, string code, string message, string severity = "error")
        => new() { Field = field, Code = code, Message = message, Severity = severity };

    static string Truncate(string text, int max)
        => string.IsNullOrEmpty(text) || text.Length <= max ? text ?? "" : text.Substring(0, max);
}
