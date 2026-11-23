using System.Text.Json.Nodes;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace CICAPOS.Register;

/// <summary>
/// Registers unbound Custom API cicapos_AiAssist + plugin step for AiAssistPlugin.
/// </summary>
static class AiAssistSetup
{
    const string ApiName = "cicapos_AiAssist";

    public static void Ensure(ServiceClient service, Guid assemblyId, Guid userId, Action<Guid, int, bool> addComponent)
    {
        _ = userId;
        var typeId = EnsureType(service, assemblyId, "CICAPOS.Plugins.AiAssistPlugin");
        var apiId = EnsureApi(service, typeId);
        EnsureRequestParams(service, apiId);
        EnsureResponseProps(service, apiId);
        EnsureNovaEnvConfig(service);
        EnsurePortalTablePermission(service);
        addComponent(typeId, 90, false);
        addComponent(apiId, 10180, false);
        Console.WriteLine("AI Assist Custom API ready: " + ApiName + " → AiAssistPlugin");
    }

    /// <summary>
    /// Writes NOVA_* from process env into Dataverse environment variable cicapos_AiAssistConfig.
    /// </summary>
    static void EnsureNovaEnvConfig(ServiceClient service)
    {
        const string schema = "cicapos_AiAssistConfig";
        var apiKey = Environment.GetEnvironmentVariable("NOVA_API_KEY");
        var ocrKey = Environment.GetEnvironmentVariable("NOVA_OCR_API_KEY")
            ?? Environment.GetEnvironmentVariable("NOVA_OCR_KEY");
        if (string.IsNullOrWhiteSpace(apiKey) && string.IsNullOrWhiteSpace(ocrKey))
        {
            Console.WriteLine("AI Assist config: set NOVA_API_KEY + NOVA_OCR_API_KEY in the shell before Register to push cicapos_AiAssistConfig.");
            return;
        }

        var blob = "{"
            + "\"NOVA_BASE_URL\":\"" + Esc(Environment.GetEnvironmentVariable("NOVA_BASE_URL") ?? "https://nova.deloitte.com.cn/v1") + "\","
            + "\"NOVA_API_KEY\":\"" + Esc(apiKey ?? "") + "\","
            + "\"NOVA_MODEL\":\"" + Esc(Environment.GetEnvironmentVariable("NOVA_MODEL") ?? "DeepSeek-V4-Flash") + "\","
            + "\"NOVA_OCR_API_KEY\":\"" + Esc(ocrKey ?? "") + "\","
            + "\"NOVA_OCR_BASE_URL\":\"" + Esc(Environment.GetEnvironmentVariable("NOVA_OCR_BASE_URL") ?? "https://nova.deloitte.com.cn/nova-cloud/ocr/ocr/v1") + "\""
            + "}";

        var defQ = new QueryExpression("environmentvariabledefinition")
        {
            ColumnSet = new ColumnSet("environmentvariabledefinitionid"),
            TopCount = 1
        };
        defQ.Criteria.AddCondition("schemaname", ConditionOperator.Equal, schema);
        var def = service.RetrieveMultiple(defQ).Entities.FirstOrDefault();
        Guid defId;
        if (def == null)
        {
            defId = service.Create(new Entity("environmentvariabledefinition")
            {
                ["schemaname"] = schema,
                ["displayname"] = "CICAPOS AI Assist (D.Nova)",
                ["description"] = "JSON: NOVA_API_KEY (ak-), NOVA_OCR_API_KEY (sk-), NOVA_BASE_URL, NOVA_MODEL",
                ["defaultvalue"] = blob,
                ["type"] = new OptionSetValue(100000000) // String
            });
        }
        else
        {
            defId = def.Id;
            service.Update(new Entity("environmentvariabledefinition", defId) { ["defaultvalue"] = blob });
        }

        var valQ = new QueryExpression("environmentvariablevalue")
        {
            ColumnSet = new ColumnSet("environmentvariablevalueid"),
            TopCount = 1
        };
        valQ.Criteria.AddCondition("environmentvariabledefinitionid", ConditionOperator.Equal, defId);
        var val = service.RetrieveMultiple(valQ).Entities.FirstOrDefault();
        if (val == null)
            service.Create(new Entity("environmentvariablevalue")
            {
                ["environmentvariabledefinitionid"] = new EntityReference("environmentvariabledefinition", defId),
                ["value"] = blob
            });
        else
            service.Update(new Entity("environmentvariablevalue", val.Id) { ["value"] = blob });

        Console.WriteLine("AI Assist env synced: " + schema + " (D.Nova keys from process environment).");
    }

    /// <summary>
    /// Power Pages enhanced model reads table permissions from powerpagecomponent type 18 JSON,
    /// including adx_entitypermission_webrole. mspp N:N links alone are not enough.
    /// </summary>
    public static void EnsurePortalTablePermission(ServiceClient service)
    {
        const string SiteId = "ea8078b2-2728-4878-9694-7992c3061c1b";
        const string PermId = "52ce98b5-f3e6-f111-84e9-6045bd5918b8";
        const int TablePermissionType = 18;
        const int SiteSettingType = 9;
        var roleIds = new[]
        {
            "ccc02cfa-5698-4d2e-a0dd-c213f9847d93", // Authenticated Users
            "b4cdd773-d6e1-f111-84ea-6045bd5918b8", // Applicant
            "5eb0973d-1e19-40b0-8a0b-e9b07cd67dfc", // Anonymous Users (Flow SPA has no portal login)
            "cba98d1d-be5f-4bb2-894e-1a05f9d97015"  // Administrators
        };
        const string CompanyUserRole = "a5100001-e2e1-4111-8111-6045bd5912f1";

        var content = new JsonObject
        {
            ["adx_entitypermission_webrole"] = new JsonArray(roleIds.Select(id => JsonValue.Create(id)).ToArray()),
            ["entityname"] = "cicapos_aiassistjob",
            ["entitylogicalname"] = "cicapos_aiassistjob",
            ["scope"] = 756150000,
            ["read"] = true,
            ["write"] = true,
            ["create"] = true,
            ["delete"] = false,
            ["append"] = false,
            ["appendto"] = false
        }.ToJsonString();

        UpsertPageComponent(service, Guid.Parse(PermId), "cicapos_aiassistjob", TablePermissionType, Guid.Parse(SiteId), content);
        Console.WriteLine("Portal table permission cicapos_aiassistjob written (type 18, Global Create/Read/Write + web roles).");

        foreach (var roleId in roleIds)
        {
            try
            {
                service.Associate("mspp_entitypermission", Guid.Parse(PermId),
                    new Relationship("mspp_entitypermission_webrole"),
                    new EntityReferenceCollection { new EntityReference("mspp_webrole", Guid.Parse(roleId)) });
            }
            catch (Exception ex) when (ex.Message.Contains("duplicate", StringComparison.OrdinalIgnoreCase)
                || ex.Message.Contains("already", StringComparison.OrdinalIgnoreCase))
            {
            }
        }

        try
        {
            service.Disassociate("mspp_entitypermission", Guid.Parse(PermId),
                new Relationship("mspp_entitypermission_webrole"),
                new EntityReferenceCollection { new EntityReference("mspp_webrole", Guid.Parse(CompanyUserRole)) });
            Console.WriteLine("Removed Company User from cicapos_aiassistjob permission.");
        }
        catch (Exception ex)
        {
            Console.WriteLine("Company User unlink: " + ex.Message.Split('\n')[0]);
        }

        UpsertSiteSettingComponent(service, Guid.Parse(SiteId), SiteSettingType,
            "24ce98b5-f3e6-f111-84e9-6045bd5918b8", "Webapi/cicapos_aiassistjob/enabled", "true");
        UpsertSiteSettingComponent(service, Guid.Parse(SiteId), SiteSettingType,
            "45ce98b5-f3e6-f111-84e9-6045bd5918b8", "Webapi/cicapos_aiassistjob/fields", "*");
        UpsertSiteSettingComponent(service, Guid.Parse(SiteId), SiteSettingType,
            "457d4ee7-e9e6-f111-84e9-6045bd5918b8", "Webapi/enable", "true");
    }

    static void UpsertPageComponent(ServiceClient service, Guid id, string name, int type, Guid siteId, string content)
    {
        Entity? existing = null;
        try { existing = service.Retrieve("powerpagecomponent", id, new ColumnSet("powerpagecomponentid", "content")); }
        catch { existing = null; }
        var row = existing ?? new Entity("powerpagecomponent", id);
        row["name"] = name;
        row["powerpagecomponenttype"] = new OptionSetValue(type);
        row["powerpagesiteid"] = new EntityReference("powerpagesite", siteId);
        row["content"] = content;
        if (existing == null) service.Create(row);
        else service.Update(row);
    }

    static void UpsertSiteSettingComponent(ServiceClient service, Guid siteId, int type, string id, string name, string value)
    {
        var content = new JsonObject { ["value"] = value, ["name"] = name, ["source"] = 0 }.ToJsonString();
        try
        {
            UpsertPageComponent(service, Guid.Parse(id), name, type, siteId, content);
            Console.WriteLine("Site setting " + name + "=" + value);
        }
        catch (Exception ex)
        {
            Console.WriteLine("Site setting " + name + ": " + ex.Message.Split('\n')[0]);
        }
    }

    static string Esc(string s) => (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"");

    static Guid EnsureType(ServiceClient service, Guid assemblyId, string typeName)
    {
        var query = new QueryExpression("plugintype") { ColumnSet = new ColumnSet("plugintypeid", "pluginassemblyid"), TopCount = 1 };
        query.Criteria.AddCondition("typename", ConditionOperator.Equal, typeName);
        var existing = service.RetrieveMultiple(query).Entities.FirstOrDefault();
        if (existing != null)
        {
            if (existing.GetAttributeValue<EntityReference>("pluginassemblyid")?.Id != assemblyId)
                service.Update(new Entity("plugintype", existing.Id) { ["pluginassemblyid"] = new EntityReference("pluginassembly", assemblyId) });
            return existing.Id;
        }
        return service.Create(new Entity("plugintype")
        {
            ["pluginassemblyid"] = new EntityReference("pluginassembly", assemblyId),
            ["typename"] = typeName,
            ["name"] = typeName,
            ["friendlyname"] = typeName
        });
    }

    static Guid EnsureApi(ServiceClient service, Guid pluginTypeId)
    {
        var query = new QueryExpression("customapi") { ColumnSet = new ColumnSet("customapiid"), TopCount = 1 };
        query.Criteria.AddCondition("uniquename", ConditionOperator.Equal, ApiName);
        var existing = service.RetrieveMultiple(query).Entities.FirstOrDefault();
        if (existing != null)
        {
            service.Update(new Entity("customapi", existing.Id)
            {
                ["plugintypeid"] = new EntityReference("plugintype", pluginTypeId),
                ["description"] = "Prefill, upload validation and form quality via D.Nova OCR + Chat (DeepSeek-V4-Flash)."
            });
            return existing.Id;
        }

        return service.Create(new Entity("customapi")
        {
            ["uniquename"] = ApiName,
            ["name"] = "CICAPOS AI Assist",
            ["displayname"] = "CICAPOS AI Assist",
            ["description"] = "Prefill, upload validation and form quality via D.Nova OCR + Chat (DeepSeek-V4-Flash).",
            ["bindingtype"] = new OptionSetValue(0), // Global
            ["isfunction"] = false,
            ["isprivate"] = false,
            ["workflowsdkstepenabled"] = false,
            ["allowedcustomprocessingsteptype"] = new OptionSetValue(0),
            ["plugintypeid"] = new EntityReference("plugintype", pluginTypeId)
        });
    }

    static void EnsureRequestParams(ServiceClient service, Guid apiId)
    {
        foreach (var (name, type, required) in new (string, int, bool)[]
        {
            ("cicapos_action", 10, true), // String
            ("cicapos_schemakey", 10, true),
            ("cicapos_doctype", 10, false),
            ("cicapos_documenttext", 10, false),
            ("cicapos_filename", 10, false),
            ("cicapos_filebase64", 10, false),
            ("cicapos_currentvaluesjson", 10, false),
            ("cicapos_contenthash", 10, false)
        })
            EnsureParam(service, apiId, name, type, required);
    }

    static void EnsureResponseProps(ServiceClient service, Guid apiId)
    {
        EnsureProp(service, apiId, "cicapos_pass", 0); // Boolean
        foreach (var name in new[] { "cicapos_source", "cicapos_model", "cicapos_fieldsjson", "cicapos_issuesjson", "cicapos_notes", "cicapos_contenthash" })
            EnsureProp(service, apiId, name, 10);
    }

    static void EnsureParam(ServiceClient service, Guid apiId, string name, int type, bool required)
    {
        var query = new QueryExpression("customapirequestparameter") { ColumnSet = new ColumnSet("customapirequestparameterid"), TopCount = 1 };
        query.Criteria.AddCondition("uniquename", ConditionOperator.Equal, name);
        query.Criteria.AddCondition("customapiid", ConditionOperator.Equal, apiId);
        if (service.RetrieveMultiple(query).Entities.Any()) return;
        service.Create(new Entity("customapirequestparameter")
        {
            ["uniquename"] = name,
            ["name"] = name,
            ["displayname"] = name,
            ["type"] = new OptionSetValue(type),
            ["isoptional"] = !required,
            ["customapiid"] = new EntityReference("customapi", apiId)
        });
    }

    static void EnsureProp(ServiceClient service, Guid apiId, string name, int type)
    {
        var query = new QueryExpression("customapiresponseproperty") { ColumnSet = new ColumnSet("customapiresponsepropertyid"), TopCount = 1 };
        query.Criteria.AddCondition("uniquename", ConditionOperator.Equal, name);
        query.Criteria.AddCondition("customapiid", ConditionOperator.Equal, apiId);
        if (service.RetrieveMultiple(query).Entities.Any()) return;
        service.Create(new Entity("customapiresponseproperty")
        {
            ["uniquename"] = name,
            ["name"] = name,
            ["displayname"] = name,
            ["type"] = new OptionSetValue(type),
            ["customapiid"] = new EntityReference("customapi", apiId)
        });
    }

}
