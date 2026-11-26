namespace CICAPOS.Plugins.Ai;

/// <summary>
/// Structured demo extractions used when Azure keys are not configured.
/// Same field keys as live Document Intelligence + gpt-4o-mini mapping.
/// </summary>
public static class AiDemoCatalog
{
    public static Dictionary<string, string> Prefill(string schemaKey, string? docType)
    {
        return (schemaKey ?? "").ToLowerInvariant() switch
        {
            AiSchemaKeys.CompanyRegistration => new(StringComparer.OrdinalIgnoreCase)
            {
                ["br"] = "68899001",
                ["branch"] = "000",
                ["brExpiry"] = "2027-12-31",
                ["nameEn"] = "Lantau Smart Construction Limited",
                ["nameTc"] = "大嶼山智慧建造有限公司",
                ["address"] = "Unit 12, 8/F, Lantau Commerce Centre, Tung Chung, New Territories",
                ["staff"] = "36",
                ["bankName"] = "004 The Hongkong and Shanghai Banking Corporation",
                ["bankAcct"] = "511-278830-838",
                ["bankHolder"] = "Lantau Smart Construction Limited",
                ["stmtDate"] = DateTime.UtcNow.Date.AddDays(-18).ToString("yyyy-MM-dd"),
                ["shareholdersJson"] = "[{\"name\":\"Lantau Holdings Limited\",\"kind\":\"Company\",\"br\":\"60011223\",\"pct\":\"70\"},{\"name\":\"Yeung Tak Man\",\"kind\":\"Individual\",\"br\":\"\",\"pct\":\"30\"}]",
                ["mgName"] = "Yeung Tak Man",
                ["mgTitle"] = "Director"
            },
            AiSchemaKeys.CitfSite => new(StringComparer.OrdinalIgnoreCase)
            {
                ["no"] = "C/2027/031",
                ["n"] = "Tung Chung New Town Extension – Site 5 Foundation Works",
                ["emp"] = "Civil Engineering and Development Department",
                ["val"] = "186500000",
                ["s"] = "2027-02-01",
                ["e"] = "2029-07-31"
            },
            AiSchemaKeys.PalProduct => new(StringComparer.OrdinalIgnoreCase)
            {
                ["name"] = "SiteEye AI Safety Camera System (4S)",
                ["model"] = "SE-400",
                ["version"] = "2026.1",
                ["spec"] = "IP66 camera with edge AI for helmet/vest and exclusion-zone detection; 24/7 monitoring with supervisor alerts within 2 seconds.",
                ["mf"] = "SmartSite Robotics HK Limited",
                ["mfCountry"] = "Hong Kong",
                ["origin"] = "Hong Kong",
                ["local"] = "Yes",
                ["functions"] = "Watches the work area 24/7; detects missing helmets and vests and people inside crane or plant exclusion zones; alerts the supervisor’s phone and the site speaker within 2 seconds; keeps a daily safety log.",
                ["innovation"] = "On-device vision model tuned for Hong Kong construction PPE and tower-crane exclusion zones.",
                ["merits"] = "Supports CITF Smart Site Safety System objectives by reducing struck-by incidents and providing auditable alerts.",
                ["obj"] = "Safety, Productivity",
                ["jobsJson"] = "[{\"project\":\"Kai Tak Sports Park\",\"client\":\"Harbour Build Engineering Limited\",\"year\":\"2025\",\"contact\":\"Carmen Lau, Project Manager\"}]",
                ["price"] = "86000",
                ["unit"] = "per set",
                ["launch"] = "2024-03-01"
            },
            AiSchemaKeys.PalCourse => new(StringComparer.OrdinalIgnoreCase)
            {
                ["name"] = "BIM for Site Engineers",
                ["model"] = "BIMT-SE-01",
                ["version"] = "2026 syllabus",
                ["spec"] = "24 hours over 6 sessions, classroom with hands-on labs; entry level: site engineers with no BIM experience; assessment: practical test, 70% pass mark.",
                ["mf"] = "SmartSite Academy",
                ["origin"] = "Hong Kong",
                ["local"] = "Yes",
                ["functions"] = "Participants can read and navigate federated models, check quantities, raise issues in a CDE and use models in site inspections.",
                ["innovation"] = "Uses the participant’s own live project models in labs.",
                ["merits"] = "Over 300 engineers trained; 92% pass rate; employers report faster model-based inspections.",
                ["obj"] = "Industry capability (manpower), Productivity",
                ["jobsJson"] = "[{\"project\":\"In-house training\",\"client\":\"Harbour Build Engineering Limited\",\"year\":\"2025\",\"contact\":\"HR Manager\"}]",
                ["price"] = "7800",
                ["unit"] = "per participant",
                ["launch"] = "2023-09-01"
            },
            AiSchemaKeys.PalAnnualReply => new(StringComparer.OrdinalIgnoreCase)
            {
                ["itemsJson"] = "[]"
            },
            AiSchemaKeys.QuotationCheck => QuotationFromDocType(docType),
            AiSchemaKeys.SupportingDoc => SupportingDocFromType(docType),
            AiSchemaKeys.DisbursementRequest => new(StringComparer.OrdinalIgnoreCase)
            {
                ["docsJson"] = "[]",
                ["paid"] = "true"
            },
            AiSchemaKeys.AnnualConfirmation => new(StringComparer.OrdinalIgnoreCase)
            {
                ["hasNc"] = "No",
                ["ncJust"] = "",
                ["prod"] = "4",
                ["safety"] = "4",
                ["quality"] = "4"
            },
            _ => new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        };
    }

    static Dictionary<string, string> SupportingDocFromType(string? docType)
    {
        if (string.Equals(docType, "blurred", StringComparison.OrdinalIgnoreCase))
        {
            return new(StringComparer.OrdinalIgnoreCase)
            {
                ["docType"] = docType ?? "blurred",
                ["readable"] = "false",
                ["note"] = "Image is blurred – text cannot be read. Please upload a clearer copy."
            };
        }

        var notes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Authorisation letter"] = "OCR: company chop and authorised signatory detected; matches vendor",
            ["Specification"] = "OCR: key parameters match the technical specification section",
            ["Catalogue"] = "OCR: product name, model and features match the form",
            ["Patent documents"] = "OCR: patent number and title extracted",
            ["Price list"] = "OCR: unit rate matches the price section",
            ["Performance comparison"] = "OCR: claimed merits cross-checked against comparison figures",
            ["Invoice"] = "Invoice amounts match the expense lines entered",
            ["Payment receipt"] = "Receipt cites the invoice number · signature and company chop detected",
            ["Bank transfer record"] = "Bank transfer amount and payee match the invoice",
            ["Purchase order"] = "PO matches the approved item",
            ["Delivery note"] = "Delivered quantity matches"
        };
        return new(StringComparer.OrdinalIgnoreCase)
        {
            ["docType"] = docType ?? "Document",
            ["readable"] = "true",
            ["note"] = notes.TryGetValue(docType ?? "", out var note) ? note : "Readable; fields match the form"
        };
    }

    static Dictionary<string, string> QuotationFromDocType(string? docType)
    {
        // Demo: first NPA quotation is intentionally ~4.3% lower than a form amount of 420000.
        var mismatch = !string.Equals(docType, "match", StringComparison.OrdinalIgnoreCase);
        return new(StringComparer.OrdinalIgnoreCase)
        {
            ["supplier"] = "SmartSite Robotics HK Limited",
            ["product"] = "AutoRebar Tying Robot ART-2",
            ["unitPrice"] = mismatch ? "402000" : "420000",
            ["qty"] = "1",
            ["amount"] = mismatch ? "402000" : "420000",
            ["formAmount"] = "420000"
        };
    }
}
