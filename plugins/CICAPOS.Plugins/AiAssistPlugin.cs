#if NETFRAMEWORK
using CICAPOS.Plugins.Ai;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace CICAPOS.Plugins;

/// <summary>
/// Unbound Custom API <c>cicapos_AiAssist</c>.
/// Live: D.Nova Chat (ak-) + OCR (sk-) from secure config / Dataverse env <c>cicapos_AiAssistConfig</c>.
/// Demo catalog only when AI_ALLOW_DEMO=true (tests).
/// </summary>
public sealed class AiAssistPlugin : IPlugin
{
    readonly string? _secureConfig;

    public AiAssistPlugin() { }

    public AiAssistPlugin(string unsecureConfig, string secureConfig)
    {
        _ = unsecureConfig;
        _secureConfig = secureConfig;
    }

    public void Execute(IServiceProvider serviceProvider)
    {
        var context = (IPluginExecutionContext)serviceProvider.GetService(typeof(IPluginExecutionContext));
        if (context.Depth > 3) return;

        if (string.Equals(context.MessageName, "Create", StringComparison.OrdinalIgnoreCase)
            && string.Equals(context.PrimaryEntityName, "cicapos_aiassistjob", StringComparison.OrdinalIgnoreCase))
        {
            ExecuteJob(serviceProvider, context);
            return;
        }

        var action = Str(context, "cicapos_action");
        var schemaKey = Str(context, "cicapos_schemakey");
        if (string.IsNullOrWhiteSpace(action) || string.IsNullOrWhiteSpace(schemaKey))
            throw new InvalidPluginExecutionException("cicapos_action and cicapos_schemakey are required.");

        var request = new AiAssistRequest
        {
            Action = action,
            SchemaKey = schemaKey,
            DocType = Str(context, "cicapos_doctype"),
            DocumentText = Str(context, "cicapos_documenttext"),
            FileName = Str(context, "cicapos_filename"),
            FileBase64 = Str(context, "cicapos_filebase64"),
            CurrentValuesJson = Str(context, "cicapos_currentvaluesjson"),
            ContentHash = Str(context, "cicapos_contenthash")
        };

        try
        {
            var factory = (IOrganizationServiceFactory)serviceProvider.GetService(typeof(IOrganizationServiceFactory));
            var org = factory.CreateOrganizationService(null);
            var configBlob = FirstNonEmpty(_secureConfig, ReadEnvVar(org, AiProviderConfig.DataverseEnvSchema));
            var config = AiProviderConfig.FromSecureConfig(configBlob);
            if (!config.HasCompatible && !config.HasBedrock && !config.AllowDemo)
                throw new InvalidPluginExecutionException(
                    "D.Nova is not configured. Set Dataverse env " + AiProviderConfig.DataverseEnvSchema
                    + " (or plugin secure config) with NOVA_API_KEY (ak-) and NOVA_OCR_API_KEY (sk-).");

            var response = new AiAssistService(config).Execute(request);
            context.OutputParameters["cicapos_pass"] = response.Pass;
            context.OutputParameters["cicapos_source"] = response.Source;
            context.OutputParameters["cicapos_model"] = response.Model ?? "";
            context.OutputParameters["cicapos_fieldsjson"] = response.FieldsJson;
            context.OutputParameters["cicapos_issuesjson"] = response.IssuesJson;
            context.OutputParameters["cicapos_notes"] = response.Notes ?? "";
            context.OutputParameters["cicapos_contenthash"] = response.ContentHash ?? "";
        }
        catch (ArgumentException ex)
        {
            throw new InvalidPluginExecutionException(ex.Message);
        }
        catch (InvalidPluginExecutionException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new InvalidPluginExecutionException("AI assist failed: " + ex.Message, ex);
        }
    }

    void ExecuteJob(IServiceProvider serviceProvider, IPluginExecutionContext context)
    {
        if (!context.InputParameters.Contains("Target") || context.InputParameters["Target"] is not Entity target)
            return;

        var request = new AiAssistRequest
        {
            Action = target.GetAttributeValue<string>("cicapos_action") ?? "",
            SchemaKey = target.GetAttributeValue<string>("cicapos_schemakey") ?? "",
            DocType = target.GetAttributeValue<string>("cicapos_doctype") ?? "",
            DocumentText = target.GetAttributeValue<string>("cicapos_documenttext") ?? "",
            FileName = target.GetAttributeValue<string>("cicapos_filename") ?? "",
            FileBase64 = target.GetAttributeValue<string>("cicapos_filebase64") ?? "",
            CurrentValuesJson = target.GetAttributeValue<string>("cicapos_currentvaluesjson") ?? "",
            ContentHash = target.GetAttributeValue<string>("cicapos_contenthash") ?? ""
        };
        if (string.IsNullOrWhiteSpace(request.Action) || string.IsNullOrWhiteSpace(request.SchemaKey))
            throw new InvalidPluginExecutionException("cicapos_action and cicapos_schemakey are required.");

        try
        {
            var factory = (IOrganizationServiceFactory)serviceProvider.GetService(typeof(IOrganizationServiceFactory));
            var org = factory.CreateOrganizationService(null);
            var configBlob = FirstNonEmpty(_secureConfig, ReadEnvVar(org, AiProviderConfig.DataverseEnvSchema));
            var config = AiProviderConfig.FromSecureConfig(configBlob);
            if (!config.HasCompatible && !config.HasBedrock && !config.AllowDemo)
                throw new InvalidOperationException(
                    "D.Nova is not configured. Set Dataverse env " + AiProviderConfig.DataverseEnvSchema + ".");

            var response = new AiAssistService(config).Execute(request);
            WriteResponse(target, response);
        }
        catch (Exception ex)
        {
            var msg = ex.Message ?? "AI assist failed";
            WriteResponse(target, new AiAssistResponse
            {
                Pass = false,
                Source = "error",
                Model = "",
                FieldsJson = "{}",
                IssuesJson = AiJson.ToArrayJson(new[]
                {
                    new AiIssue { Field = "readable", Code = "ai_failed", Message = msg, Severity = "error" }
                }),
                Notes = msg,
                ContentHash = request.ContentHash ?? ""
            });
        }
    }

    static void WriteResponse(Entity target, AiAssistResponse response)
    {
        target["cicapos_pass"] = response.Pass;
        target["cicapos_source"] = response.Source ?? "";
        target["cicapos_model"] = response.Model ?? "";
        target["cicapos_fieldsjson"] = response.FieldsJson ?? "{}";
        target["cicapos_issuesjson"] = response.IssuesJson ?? "[]";
        target["cicapos_notes"] = Truncate(response.Notes ?? "", 4000);
        target["cicapos_contenthash"] = response.ContentHash ?? "";
        target["cicapos_filebase64"] = "";
    }

    static string Truncate(string text, int max)
        => text.Length <= max ? text : text.Substring(0, max);

    static string? ReadEnvVar(IOrganizationService service, string schemaName)
    {
        try
        {
            var defQ = new QueryExpression("environmentvariabledefinition")
            {
                ColumnSet = new ColumnSet("environmentvariabledefinitionid", "defaultvalue"),
                TopCount = 1
            };
            defQ.Criteria.AddCondition("schemaname", ConditionOperator.Equal, schemaName);
            var def = service.RetrieveMultiple(defQ).Entities.FirstOrDefault();
            if (def == null) return null;

            var valQ = new QueryExpression("environmentvariablevalue")
            {
                ColumnSet = new ColumnSet("value"),
                TopCount = 1
            };
            valQ.Criteria.AddCondition("environmentvariabledefinitionid", ConditionOperator.Equal, def.Id);
            var val = service.RetrieveMultiple(valQ).Entities.FirstOrDefault()?.GetAttributeValue<string>("value");
            return FirstNonEmpty(val, def.GetAttributeValue<string>("defaultvalue"));
        }
        catch
        {
            return null;
        }
    }

    static string? FirstNonEmpty(params string?[] values)
    {
        foreach (var v in values)
            if (!string.IsNullOrWhiteSpace(v)) return v;
        return null;
    }

    static string Str(IPluginExecutionContext context, string name)
        => context.InputParameters.Contains(name) && context.InputParameters[name] != null
            ? Convert.ToString(context.InputParameters[name]) ?? ""
            : "";
}
#endif
