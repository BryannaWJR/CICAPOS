using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace CICAPOS.Register;

/// <summary>
/// Publishes Flow SPA + D.Nova keys to Power Pages / Dataverse.
/// </summary>
static class AiPortalDeploy
{
    const string FlowCopyId = "a5100001-e2e1-4111-8111-6045bd5910f2";
    const string SiteId = "ea8078b2-2728-4878-9694-7992c3061c1b";

    public static void Run(ServiceClient service, Guid userId, string pluginDll)
    {
        _ = userId;
        Console.WriteLine("AI portal deploy: syncing D.Nova env + Flow page + plugin…");

        // 1) Plugin assembly + Custom API + env keys (NOVA_* must be in process env)
        var assemblyName = System.Reflection.AssemblyName.GetAssemblyName(pluginDll);
        var tokenBytes = assemblyName.GetPublicKeyToken() ?? Array.Empty<byte>();
        var token = Convert.ToHexString(tokenBytes).ToLowerInvariant();
        var version = assemblyName.Version?.ToString() ?? "1.0.1.0";
        var assemblyId = UploadAssembly(service, pluginDll, assemblyName.Name ?? "CICAPOS.Plugins", token, version);
        AiAssistSetup.Ensure(service, assemblyId, userId, (_, __, ___) => { });

        // 2) Flow webpage HTML (inject runtime Nova keys for browser-direct OCR+Chat)
        var htmlPath = FindFlowHtml();
        var html = File.ReadAllText(htmlPath);
        html = InjectNovaBootstrap(html);
        UpdateFlowPage(service, html);

        // 3) Publish Dataverse customizations
        try
        {
            service.Execute(new PublishAllXmlRequest());
            Console.WriteLine("Published Dataverse customizations.");
        }
        catch (Exception ex)
        {
            Console.WriteLine("Publish warning: " + ex.Message);
        }

        Console.WriteLine("AI portal deploy done. Open /flow/ and hard-refresh (Cmd+Shift+R).");
        Console.WriteLine("Site: https://site-9m48j.powerappsportals.com/flow/");
    }

    static string FindFlowHtml()
    {
        var candidates = new[]
        {
            Path.GetFullPath("portal/cic-portal-app---site-9m48j/web-pages/flow/content-pages/Flow.en-US.webpage.copy.html"),
            Path.GetFullPath("../portal/cic-portal-app---site-9m48j/web-pages/flow/content-pages/Flow.en-US.webpage.copy.html"),
            Path.GetFullPath("../../portal/cic-portal-app---site-9m48j/web-pages/flow/content-pages/Flow.en-US.webpage.copy.html"),
            Path.GetFullPath("solution/CICAPOS/src/WebResources/cicapos_d365ops.html")
        };
        foreach (var c in candidates)
            if (File.Exists(c)) return c;
        throw new FileNotFoundException("Flow HTML not found (Flow.en-US.webpage.copy.html / cicapos_d365ops.html).");
    }

    static string InjectNovaBootstrap(string html)
    {
        var apiKey = Environment.GetEnvironmentVariable("NOVA_API_KEY") ?? "";
        var ocrKey = Environment.GetEnvironmentVariable("NOVA_OCR_API_KEY")
            ?? Environment.GetEnvironmentVariable("NOVA_OCR_KEY") ?? "";
        if (string.IsNullOrWhiteSpace(apiKey) && string.IsNullOrWhiteSpace(ocrKey))
        {
            Console.WriteLine("WARNING: NOVA_API_KEY / NOVA_OCR_API_KEY not set – Flow page uploaded without browser keys.");
            return html;
        }

        var bootstrap = "\n<script>\n"
            + "window.CITF_AI_LIVE = true;\n"
            + "window.CITF_NOVA = {\n"
            + "  baseUrl: " + JsStr(Environment.GetEnvironmentVariable("NOVA_BASE_URL") ?? "https://nova.deloitte.com.cn/v1") + ",\n"
            + "  apiKey: " + JsStr(apiKey) + ",\n"
            + "  model: " + JsStr(Environment.GetEnvironmentVariable("NOVA_MODEL") ?? "DeepSeek-V4-Flash") + ",\n"
            + "  ocrBaseUrl: " + JsStr(Environment.GetEnvironmentVariable("NOVA_OCR_BASE_URL") ?? "https://nova.deloitte.com.cn/nova-cloud/ocr/ocr/v1") + ",\n"
            + "  ocrApiKey: " + JsStr(ocrKey) + "\n"
            + "};\n"
            + "try { if (window.CitfAi && CitfAi.saveNovaConfig) CitfAi.saveNovaConfig(window.CITF_NOVA); } catch (e) {}\n"
            + "</script>\n";

        // Never splice on "</body>" — that substring appears inside JS letter HTML templates.
        var endScript = html.LastIndexOf("</script>", StringComparison.OrdinalIgnoreCase);
        var endRaw = html.LastIndexOf("{% endraw %}", StringComparison.OrdinalIgnoreCase);
        if (endScript >= 0 && endRaw > endScript)
            return html.Substring(0, endScript + "</script>".Length) + bootstrap + html.Substring(endScript + "</script>".Length);
        if (endRaw >= 0)
            return html.Substring(0, endRaw) + bootstrap + html.Substring(endRaw);
        return html + bootstrap;
    }

    static string JsStr(string s) => "\"" + (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "").Replace("\n", "\\n") + "\"";

    static void UpdateFlowPage(ServiceClient service, string html)
    {
        var id = Guid.Parse(FlowCopyId);
        var page = service.Retrieve("powerpagecomponent", id, new ColumnSet("content", "name"));
        var raw = page.GetAttributeValue<string>("content") ?? "{}";
        JsonObject node;
        try { node = JsonNode.Parse(raw)!.AsObject(); }
        catch { node = new JsonObject(); }

        node["copy"] = html;
        node["title"] = node["title"]?.GetValue<string>() ?? "Role flow";
        node["partialurl"] = node["partialurl"]?.GetValue<string>() ?? "flow";
        if (node["powerpagesiteid"] == null)
            node["powerpagesiteid"] = SiteId;

        service.Update(new Entity("powerpagecomponent", id)
        {
            ["content"] = node.ToJsonString(new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping })
        });
        Console.WriteLine("Updated Flow webpage copy (" + html.Length + " chars HTML).");
    }

    static Guid UploadAssembly(ServiceClient service, string path, string name, string token, string version)
    {
        // Mirror Program.UploadAssembly – keep deploy self-contained.
        var query = new QueryExpression("pluginassembly")
        {
            ColumnSet = new ColumnSet("pluginassemblyid", "version"),
            TopCount = 1
        };
        query.Criteria.AddCondition("name", ConditionOperator.Equal, name);
        var existing = service.RetrieveMultiple(query).Entities.FirstOrDefault();
        var content = Convert.ToBase64String(File.ReadAllBytes(path));
        if (existing != null)
        {
            service.Update(new Entity("pluginassembly", existing.Id)
            {
                ["content"] = content,
                ["version"] = version,
                ["publickeytoken"] = token,
                ["sourcetype"] = new OptionSetValue(0),
                ["isolationmode"] = new OptionSetValue(2)
            });
            Console.WriteLine("Updated plugin assembly " + existing.Id);
            return existing.Id;
        }
        var id = service.Create(new Entity("pluginassembly")
        {
            ["name"] = name,
            ["content"] = content,
            ["version"] = version,
            ["publickeytoken"] = token,
            ["sourcetype"] = new OptionSetValue(0),
            ["isolationmode"] = new OptionSetValue(2)
        });
        Console.WriteLine("Created plugin assembly " + id);
        return id;
    }
}
