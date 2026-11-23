using System.Runtime.Serialization;

namespace CICAPOS.Plugins.Ai;

public static class AiActions
{
    public const string Prefill = "prefill";
    public const string ValidateUpload = "validate_upload";
    public const string ValidateForm = "validate_form";
    public const string Suggest = "suggest";
}

public static class AiSchemaKeys
{
    public const string CompanyRegistration = "company_registration";
    public const string CitfSite = "citf_site";
    public const string FundingApplication = "funding_application";
    public const string FundingItemPlan = "funding_item_plan";
    public const string PalProduct = "pal_product";
    public const string PalCourse = "pal_course";
    public const string PalAnnualReply = "pal_annual_reply";
    public const string QuotationCheck = "quotation_check";
    public const string SupportingDoc = "supporting_doc";
    public const string DisbursementRequest = "disbursement_request";
    public const string AnnualConfirmation = "annual_confirmation";
}

[DataContract]
public sealed class AiAssistRequest
{
    [DataMember(Name = "action")] public string Action { get; set; } = "";
    [DataMember(Name = "schemaKey")] public string SchemaKey { get; set; } = "";
    [DataMember(Name = "docType")] public string? DocType { get; set; }
    [DataMember(Name = "documentText")] public string? DocumentText { get; set; }
    [DataMember(Name = "fileName")] public string? FileName { get; set; }
    [DataMember(Name = "fileBase64")] public string? FileBase64 { get; set; }
    [DataMember(Name = "currentValuesJson")] public string? CurrentValuesJson { get; set; }
    [DataMember(Name = "contentHash")] public string? ContentHash { get; set; }
}

[DataContract]
public sealed class AiIssue
{
    [DataMember(Name = "field")] public string Field { get; set; } = "";
    [DataMember(Name = "code")] public string Code { get; set; } = "";
    [DataMember(Name = "message")] public string Message { get; set; } = "";
    [DataMember(Name = "severity")] public string Severity { get; set; } = "error";
}

[DataContract]
public sealed class AiAssistResponse
{
    [DataMember(Name = "pass")] public bool Pass { get; set; }
    [DataMember(Name = "source")] public string Source { get; set; } = "demo";
    [DataMember(Name = "model")] public string? Model { get; set; }
    [DataMember(Name = "fieldsJson")] public string FieldsJson { get; set; } = "{}";
    [DataMember(Name = "issuesJson")] public string IssuesJson { get; set; } = "[]";
    [DataMember(Name = "notes")] public string? Notes { get; set; }
    [DataMember(Name = "contentHash")] public string? ContentHash { get; set; }
}

public sealed class AiFieldSpec
{
    public required string Key { get; init; }
    public required string Label { get; init; }
    public string Type { get; init; } = "string";
    public bool Required { get; init; }
}

public sealed class AiFormSchema
{
    public required string Key { get; init; }
    public required string Title { get; init; }
    public required IReadOnlyList<AiFieldSpec> Fields { get; init; }
}
