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
        PublishLanguage(service);

        // 2b) CIC Operation Platform web resource + sitemap cache-bust
        UploadD365Ops(service);
        TryBumpSitemap(service);

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
        Console.WriteLine("D365 ops: CIC Operation Platform → Funding Applications, Ctrl+F5.");
    }

    static void UploadD365Ops(ServiceClient service)
    {
        var path = new[]
        {
            Path.GetFullPath("solution/CICAPOS/src/WebResources/cicapos_d365ops.html"),
            Path.GetFullPath("../solution/CICAPOS/src/WebResources/cicapos_d365ops.html"),
            Path.GetFullPath("../../solution/CICAPOS/src/WebResources/cicapos_d365ops.html")
        }.FirstOrDefault(File.Exists);
        if (path == null)
        {
            Console.WriteLine("cicapos_d365ops.html not found — skip WR upload.");
            return;
        }

        var content = Convert.ToBase64String(File.ReadAllBytes(path));
        var wrId = Guid.Parse("a5100001-e2e1-4111-8111-6045bd5910d5");
        Entity? row = null;
        try { row = service.Retrieve("webresource", wrId, new ColumnSet("name")); }
        catch { /* lookup by name */ }
        if (row == null)
        {
            var q = new QueryExpression("webresource")
            {
                ColumnSet = new ColumnSet("name"),
                TopCount = 1
            };
            q.Criteria.AddCondition("name", ConditionOperator.Equal, "cicapos_d365ops.html");
            row = service.RetrieveMultiple(q).Entities.FirstOrDefault();
        }
        if (row == null)
        {
            wrId = service.Create(new Entity("webresource")
            {
                ["name"] = "cicapos_d365ops.html",
                ["displayname"] = "CITF Operation Platform UI",
                ["webresourcetype"] = new OptionSetValue(1),
                ["content"] = content
            });
            Console.WriteLine("Created web resource cicapos_d365ops.html " + wrId);
            return;
        }
        service.Update(new Entity("webresource", row.Id) { ["content"] = content });
        Console.WriteLine("Updated web resource cicapos_d365ops.html (" + new FileInfo(path).Length + " bytes).");
    }

    static void TryBumpSitemap(ServiceClient service)
    {
        try
        {
            var q = new QueryExpression("sitemap")
            {
                ColumnSet = new ColumnSet("sitemapxml", "sitemapname"),
                TopCount = 5
            };
            q.Criteria.AddCondition("sitemapname", ConditionOperator.Equal, "cicapos_CICCRMAPP");
            var row = service.RetrieveMultiple(q).Entities.FirstOrDefault();
            if (row == null)
            {
                Console.WriteLine("Sitemap cicapos_CICCRMAPP not found — skip v=25.");
                return;
            }
            var xml = row.GetAttributeValue<string>("sitemapxml") ?? "";
            if (xml.Contains("&amp;v=25") || xml.Contains("&v=25") || xml.Contains("v=25"))
            {
                Console.WriteLine("Sitemap already at v=25.");
                return;
            }
            var next = xml.Replace("&amp;v=24", "&amp;v=25").Replace("&v=24", "&v=25").Replace("v=24", "v=25");
            if (next == xml)
            {
                Console.WriteLine("Sitemap has no v=24 token — skip.");
                return;
            }
            service.Update(new Entity("sitemap", row.Id) { ["sitemapxml"] = next });
            Console.WriteLine("Updated sitemap cache-bust to v=25.");
        }
        catch (Exception ex)
        {
            Console.WriteLine("Sitemap bump skipped: " + ex.Message);
        }
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

    public static void PublishLanguage(ServiceClient service)
    {
        var htmlPath = FindFlowHtml();
        UpdateFlowPage(service, InjectNovaBootstrap(File.ReadAllText(htmlPath)));
        TryUpdateHeader(service);
        TryUpdateHome(service);
        TryUpdateRegister(service);
        TryUpdateCss(service);
    }

    static void TryUpdateHeader(ServiceClient service)
    {
        var headerPath = new[]
        {
            Path.GetFullPath("portal/cic-portal-app---site-9m48j/web-templates/header/Header.webtemplate.source.html"),
            Path.GetFullPath("../portal/cic-portal-app---site-9m48j/web-templates/header/Header.webtemplate.source.html")
        }.FirstOrDefault(File.Exists);
        if (headerPath == null) return;
        var source = File.ReadAllText(headerPath);
        try
        {
            var id = Guid.Parse("8dfd6458-7b97-472a-bf62-6be999edbc06");
            service.Update(new Entity("adx_webtemplate", id) { ["adx_source"] = source });
            Console.WriteLine("Updated Header web template (adx_webtemplate).");
            return;
        }
        catch (Exception ex)
        {
            Console.WriteLine("Header adx_webtemplate skip: " + ex.Message);
        }
        try
        {
            var q = new QueryExpression("powerpagecomponent")
            {
                ColumnSet = new ColumnSet("content", "name"),
                TopCount = 5
            };
            q.Criteria.AddCondition("name", ConditionOperator.Equal, "Header");
            var row = service.RetrieveMultiple(q).Entities.FirstOrDefault();
            if (row == null) return;
            var raw = row.GetAttributeValue<string>("content") ?? "{}";
            JsonObject node;
            try { node = JsonNode.Parse(raw)!.AsObject(); }
            catch { node = new JsonObject(); }
            node["source"] = source;
            service.Update(new Entity("powerpagecomponent", row.Id)
            {
                ["content"] = node.ToJsonString(new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping })
            });
            Console.WriteLine("Updated Header powerpagecomponent.");
        }
        catch (Exception ex)
        {
            Console.WriteLine("Header update skipped: " + ex.Message);
        }
    }

    static void TryUpdateHome(ServiceClient service)
    {
        var homePath = new[]
        {
            Path.GetFullPath("portal/cic-portal-app---site-9m48j/web-pages/home/content-pages/Home.en-US.webpage.copy.html"),
            Path.GetFullPath("../portal/cic-portal-app---site-9m48j/web-pages/home/content-pages/Home.en-US.webpage.copy.html")
        }.FirstOrDefault(File.Exists);
        if (homePath == null) return;
        var html = File.ReadAllText(homePath);
        try
        {
            var id = Guid.Parse("165c3e75-3900-436f-a522-7be187441c9e");
            var page = service.Retrieve("powerpagecomponent", id, new ColumnSet("content", "name"));
            var raw = page.GetAttributeValue<string>("content") ?? "{}";
            JsonObject node;
            try { node = JsonNode.Parse(raw)!.AsObject(); }
            catch { node = new JsonObject(); }
            node["copy"] = html;
            service.Update(new Entity("powerpagecomponent", id)
            {
                ["content"] = node.ToJsonString(new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping })
            });
            Console.WriteLine("Updated Home webpage copy (" + html.Length + " chars HTML).");
        }
        catch (Exception ex)
        {
            Console.WriteLine("Home page update skipped: " + ex.Message);
        }
    }

    static void TryUpdateRegister(ServiceClient service)
    {
        var path = new[]
        {
            Path.GetFullPath("portal/cic-portal-app---site-9m48j/web-pages/register-company/content-pages/Register-Company.en-US.webpage.copy.html"),
            Path.GetFullPath("../portal/cic-portal-app---site-9m48j/web-pages/register-company/content-pages/Register-Company.en-US.webpage.copy.html")
        }.FirstOrDefault(File.Exists);
        if (path == null) return;
        var html = File.ReadAllText(path);
        try
        {
            var id = Guid.Parse("a5100001-e2e1-4111-8111-6045bd591061");
            var page = service.Retrieve("powerpagecomponent", id, new ColumnSet("content", "name"));
            var raw = page.GetAttributeValue<string>("content") ?? "{}";
            JsonObject node;
            try { node = JsonNode.Parse(raw)!.AsObject(); }
            catch { node = new JsonObject(); }
            node["copy"] = html;
            service.Update(new Entity("powerpagecomponent", id)
            {
                ["content"] = node.ToJsonString(new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping })
            });
            Console.WriteLine("Updated Register webpage copy (" + html.Length + " chars HTML).");
        }
        catch (Exception ex)
        {
            Console.WriteLine("Register page update skipped: " + ex.Message);
        }
    }

    static void TryUpdateCss(ServiceClient service)
    {
        var path = new[]
        {
            Path.GetFullPath("portal/cic-portal-app---site-9m48j/web-files/citf-portal.css"),
            Path.GetFullPath("../portal/cic-portal-app---site-9m48j/web-files/citf-portal.css")
        }.FirstOrDefault(File.Exists);
        if (path == null) return;
        var css = File.ReadAllText(path);
        try
        {
            var q = new QueryExpression("annotation")
            {
                ColumnSet = new ColumnSet("annotationid", "filename"),
                TopCount = 5
            };
            q.Criteria.AddCondition("objectid", ConditionOperator.Equal, Guid.Parse("a5100001-e2e1-4111-8111-6045bd591090"));
            q.Criteria.AddCondition("isdocument", ConditionOperator.Equal, true);
            var notes = service.RetrieveMultiple(q).Entities;
            var note = notes.FirstOrDefault(n => string.Equals(n.GetAttributeValue<string>("filename"), "citf-portal.css", StringComparison.OrdinalIgnoreCase))
                ?? notes.FirstOrDefault();
            if (note == null)
            {
                Console.WriteLine("CSS annotation not found.");
                return;
            }
            service.Update(new Entity("annotation", note.Id)
            {
                ["documentbody"] = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(css)),
                ["mimetype"] = "text/css",
                ["filename"] = "citf-portal.css"
            });
            Console.WriteLine("Updated citf-portal.css (" + css.Length + " chars).");
        }
        catch (Exception ex)
        {
            Console.WriteLine("CSS update skipped: " + ex.Message);
        }
    }

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
