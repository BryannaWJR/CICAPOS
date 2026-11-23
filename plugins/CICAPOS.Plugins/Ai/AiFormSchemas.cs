namespace CICAPOS.Plugins.Ai;

public static class AiFormSchemas
{
    static readonly IReadOnlyDictionary<string, AiFormSchema> All = new Dictionary<string, AiFormSchema>(StringComparer.OrdinalIgnoreCase)
    {
        [AiSchemaKeys.CompanyRegistration] = new()
        {
            Key = AiSchemaKeys.CompanyRegistration,
            Title = "Company registration (BR + NAR1 + bank statement)",
            Fields =
            [
                F("br", "BR number", required: true),
                F("branch", "Branch code", required: true),
                F("brExpiry", "BR expiry date", "date", true),
                F("nameEn", "Legal name (English)", required: true),
                F("nameTc", "Legal name (Traditional Chinese)"),
                F("address", "Registered office address", required: true),
                F("staff", "Number of persons directly employed", "number"),
                F("bankName", "Bank", required: true),
                F("bankAcct", "Account number", required: true),
                F("bankHolder", "Account holder name", required: true),
                F("stmtDate", "Bank statement date", "date"),
                F("shareholdersJson", "Shareholders (≥30%)", "json"),
                F("mgName", "Management representative name"),
                F("mgTitle", "Management representative title")
            ]
        },
        [AiSchemaKeys.CitfSite] = new()
        {
            Key = AiSchemaKeys.CitfSite,
            Title = "CITF project / site from award letter",
            Fields =
            [
                F("no", "PWP or contract number", required: true),
                F("n", "Project name", required: true),
                F("emp", "Employer / client"),
                F("val", "Contract value (HK$)", "number"),
                F("s", "Start date", "date"),
                F("e", "Completion date", "date")
            ]
        },
        [AiSchemaKeys.FundingApplication] = new()
        {
            Key = AiSchemaKeys.FundingApplication,
            Title = "Funding application quality check",
            Fields =
            [
                F("objective", "Project objective, benefits and implementation", "textarea", true),
                F("itemsJson", "Items with implementation plans", "json", true)
            ]
        },
        [AiSchemaKeys.FundingItemPlan] = new()
        {
            Key = AiSchemaKeys.FundingItemPlan,
            Title = "Item implementation plan",
            Fields =
            [
                F("product", "Item name", required: true),
                F("plan", "Implementation plan", "textarea", true)
            ]
        },
        [AiSchemaKeys.PalProduct] = new()
        {
            Key = AiSchemaKeys.PalProduct,
            Title = "PAL product listing (9 sections)",
            Fields =
            [
                F("name", "Product name", required: true),
                F("model", "Model"),
                F("version", "Version"),
                F("spec", "Specification", "textarea", true),
                F("mf", "Manufacturer", required: true),
                F("mfCountry", "Manufacturer country"),
                F("origin", "Country of origin"),
                F("local", "Locally developed (Yes/No)"),
                F("functions", "Core functions", "textarea", true),
                F("innovation", "Innovation", "textarea"),
                F("merits", "Merits against CITF objectives", "textarea"),
                F("price", "Selling price (HK$)", "number", true),
                F("unit", "Unit"),
                F("launch", "First launch date", "date")
            ]
        },
        [AiSchemaKeys.PalCourse] = new()
        {
            Key = AiSchemaKeys.PalCourse,
            Title = "PAL course listing (9 sections)",
            Fields =
            [
                F("name", "Course name", required: true),
                F("model", "Course code"),
                F("version", "Syllabus version"),
                F("spec", "Course specification", "textarea", true),
                F("mf", "Provider", required: true),
                F("origin", "Country of origin"),
                F("local", "Locally developed (Yes/No)"),
                F("functions", "Learning outcomes", "textarea", true),
                F("innovation", "Innovation", "textarea"),
                F("merits", "Merits against CITF objectives", "textarea"),
                F("price", "Price per participant (HK$)", "number", true),
                F("unit", "Unit"),
                F("launch", "First launch date", "date")
            ]
        },
        [AiSchemaKeys.PalAnnualReply] = new()
        {
            Key = AiSchemaKeys.PalAnnualReply,
            Title = "PAL annual review reply letter",
            Fields =
            [
                F("itemsJson", "Per-item price, change and justification", "json", true)
            ]
        },
        [AiSchemaKeys.QuotationCheck] = new()
        {
            Key = AiSchemaKeys.QuotationCheck,
            Title = "Quotation vs form amount",
            Fields =
            [
                F("supplier", "Supplier"),
                F("product", "Product on quotation"),
                F("unitPrice", "Unit price on quotation", "number"),
                F("qty", "Quantity on quotation", "number"),
                F("amount", "Total amount on quotation", "number", true),
                F("formAmount", "Amount on application form", "number", true)
            ]
        },
        [AiSchemaKeys.SupportingDoc] = new()
        {
            Key = AiSchemaKeys.SupportingDoc,
            Title = "Supporting document readability / consistency (PAL matrix + funding evidence)",
            Fields =
            [
                F("docType", "Document type", required: true),
                F("readable", "Text readable (true/false)", "bool", true),
                F("note", "AI note", "textarea")
            ]
        },
        [AiSchemaKeys.DisbursementRequest] = new()
        {
            Key = AiSchemaKeys.DisbursementRequest,
            Title = "Disbursement evidence automatic check",
            Fields =
            [
                F("docsJson", "Evidence documents with AI flags", "json", true),
                F("paid", "Payment completed (true/false)", "bool", true)
            ]
        },
        [AiSchemaKeys.AnnualConfirmation] = new()
        {
            Key = AiSchemaKeys.AnnualConfirmation,
            Title = "Annual confirmation automatic check",
            Fields =
            [
                F("hasNc", "Non-compliance reported (Yes/No)", required: true),
                F("ncJust", "Non-compliance justification", "textarea"),
                F("prod", "Productivity score (1–5)", "number", true),
                F("safety", "Safety score (1–5)", "number", true),
                F("quality", "Quality score (1–5)", "number", true)
            ]
        }
    };

    public static AiFormSchema Get(string schemaKey)
        => All.TryGetValue(schemaKey ?? "", out var schema)
            ? schema
            : throw new ArgumentException("Unknown AI form schema: " + schemaKey);

    public static IEnumerable<AiFormSchema> List() => All.Values;

    static AiFieldSpec F(string key, string label, string type = "string", bool required = false)
        => new() { Key = key, Label = label, Type = type, Required = required };
}
