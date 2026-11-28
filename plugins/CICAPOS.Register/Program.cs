using System.Reflection;
using System.ServiceModel;
using CICAPOS.Plugins;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;

const string Solution = "CICAPOS";
const string Marker = "CICAPOS plugin test";
const string Url = "https://org1f690222.crm5.dynamics.com";

var dll = args.FirstOrDefault(arg => arg.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
    ?? Path.GetFullPath("plugins/CICAPOS.Plugins/bin/Release/net462/CICAPOS.Plugins.dll");
if (!args.Contains("--lang") && !File.Exists(dll))
{
    Console.Error.WriteLine("Plugin assembly not found: " + dll);
    return 1;
}

var token = "";
var version = "1.0.1.0";
AssemblyName? assemblyName = null;
if (!args.Contains("--lang") || File.Exists(dll))
{
    assemblyName = AssemblyName.GetAssemblyName(dll);
    var tokenBytes = assemblyName.GetPublicKeyToken();
    if (tokenBytes == null || tokenBytes.Length == 0)
    {
        Console.Error.WriteLine("Plugin assembly is not signed.");
        return 1;
    }
    token = Convert.ToHexString(tokenBytes).ToLowerInvariant();
    version = assemblyName.Version?.ToString() ?? "1.0.1.0";
}

Console.WriteLine("Connecting...");
var accessToken = Environment.GetEnvironmentVariable("CICAPOS_ACCESS_TOKEN");
var connection = string.IsNullOrWhiteSpace(accessToken)
    ? $"AuthType=OAuth;Url={Url};SkipDiscovery=true;Authority=https://login.microsoftonline.com/common;AppId=51f81489-12ee-4a9e-aaae-a2591f45987d;RedirectUri=http://localhost;LoginPrompt=Never;TokenCacheStorePath=/tmp/cicapossync/dvcache.bin;RequireNewInstance=true"
    : $"AuthType=OAuth;Url={Url};SkipDiscovery=true;AccessToken={accessToken};LoginPrompt=Never;RequireNewInstance=true";
using var service = new ServiceClient(connection);
if (!service.IsReady)
{
    Console.Error.WriteLine(service.LastError);
    return 1;
}
var userId = ((WhoAmIResponse)service.Execute(new WhoAmIRequest())).UserId;
Console.WriteLine("Connected as " + userId);
if (args.Contains("--portal"))
{
    try
    {
        PortalSetup.Run(service, userId);
        Environment.Exit(0);
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine(Describe(ex));
        if (!string.IsNullOrWhiteSpace(service.LastError))
            Console.Error.WriteLine(service.LastError);
        Environment.Exit(1);
    }
}
if (args.Contains("--ai-diag"))
{
    try
    {
        DumpAiDiag(service);
        Environment.Exit(0);
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine(Describe(ex));
        Environment.Exit(1);
    }
}
if (args.Contains("--ai-perm"))
{
    try
    {
        CICAPOS.Register.AiAssistSetup.EnsurePortalTablePermission(service);
        DumpAiTraces(service);
        Environment.Exit(0);
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine(Describe(ex));
        if (!string.IsNullOrWhiteSpace(service.LastError))
            Console.Error.WriteLine(service.LastError);
        Environment.Exit(1);
    }
}
if (args.Contains("--committee-mail"))
{
    try
    {
        var assemblyId = UploadAssembly(service, dll, assemblyName?.Name ?? "CICAPOS.Plugins", token, version);
        const string typeName = "CICAPOS.Plugins.CommitteeEmailPlugin";
        var typeId = EnsureType(service, assemblyId, typeName);
        var pre = EnsureStep(service, typeId, userId, typeName, "email", "Create", 20, 5);
        var post = EnsureStep(service, typeId, userId, typeName, "email", "Create", 40, 5);
        AddComponent(service, assemblyId, 91, false);
        AddComponent(service, pre, 92, false);
        AddComponent(service, post, 92, false);
        Console.WriteLine("Committee email plugin registered.");
        Environment.Exit(0);
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine(Describe(ex));
        if (!string.IsNullOrWhiteSpace(service.LastError))
            Console.Error.WriteLine(service.LastError);
        Environment.Exit(1);
    }
}
if (args.Contains("--lang"))
{
    try
    {
        CICAPOS.Register.AiPortalDeploy.PublishLanguage(service);
        Environment.Exit(0);
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine(Describe(ex));
        if (!string.IsNullOrWhiteSpace(service.LastError))
            Console.Error.WriteLine(service.LastError);
        Environment.Exit(1);
    }
}
if (args.Contains("--ai-deploy"))
{
    try
    {
        CICAPOS.Register.AiPortalDeploy.Run(service, userId, dll);
        Environment.Exit(0);
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine(Describe(ex));
        if (!string.IsNullOrWhiteSpace(service.LastError))
            Console.Error.WriteLine(service.LastError);
        Environment.Exit(1);
    }
}

var parked = new List<Guid>();
var exitCode = 0;
try
{
    var assemblyId = UploadAssembly(service, dll, assemblyName?.Name ?? "CICAPOS.Plugins", token, version);
    var typeIds = new Dictionary<string, Guid>(StringComparer.Ordinal);
    foreach (var typeName in new[] { "CICAPOS.Plugins.FundingRoutePlugin", "CICAPOS.Plugins.AssignmentPlugin", "CICAPOS.Plugins.DisbursementRoutePlugin" })
        typeIds[typeName] = EnsureType(service, assemblyId, typeName);
    var steps = new (string Type, string Entity, string Message, int Stage, int Rank)[]
    {
        ("CICAPOS.Plugins.FundingRoutePlugin", "cicapos_fundingapplication", "Create", 20, 10),
        ("CICAPOS.Plugins.FundingRoutePlugin", "cicapos_fundingapplication", "Update", 20, 10),
        ("CICAPOS.Plugins.FundingRoutePlugin", "cicapos_fundingitem", "Create", 20, 10),
        ("CICAPOS.Plugins.FundingRoutePlugin", "cicapos_fundingitem", "Update", 20, 10),
        ("CICAPOS.Plugins.AssignmentPlugin", "cicapos_fundingapplication", "Create", 20, 20),
        ("CICAPOS.Plugins.AssignmentPlugin", "cicapos_fundingapplication", "Update", 20, 20),
        ("CICAPOS.Plugins.AssignmentPlugin", "cicapos_disbursementrequest", "Create", 20, 20),
        ("CICAPOS.Plugins.AssignmentPlugin", "cicapos_disbursementrequest", "Update", 20, 20),
        ("CICAPOS.Plugins.AssignmentPlugin", "cicapos_palapplication", "Create", 20, 20),
        ("CICAPOS.Plugins.AssignmentPlugin", "cicapos_palapplication", "Update", 20, 20),
        ("CICAPOS.Plugins.DisbursementRoutePlugin", "cicapos_disbursementrequest", "Create", 20, 10),
        ("CICAPOS.Plugins.DisbursementRoutePlugin", "cicapos_disbursementrequest", "Update", 20, 10),
        ("CICAPOS.Plugins.DisbursementRoutePlugin", "cicapos_disbursementrequest", "Create", 40, 10),
        ("CICAPOS.Plugins.DisbursementRoutePlugin", "cicapos_disbursementrequest", "Update", 40, 10)
    };
    var stepIds = new List<Guid>();
    foreach (var step in steps)
        stepIds.Add(EnsureStep(service, typeIds[step.Type], userId, step.Type, step.Entity, step.Message, step.Stage, step.Rank));
    AddComponent(service, assemblyId, 91, false);
    foreach (var stepId in stepIds)
        AddComponent(service, stepId, 92, false);
    SeedRules(service);
    try
    {
        CICAPOS.Register.AiAssistSetup.Ensure(service, assemblyId, userId, (id, type, required) => AddComponent(service, id, type, required));
    }
    catch (Exception ex)
    {
        Console.WriteLine("AI Assist setup skipped/partial: " + Describe(ex).Split('|')[0].Trim());
    }
    if (!args.Contains("--skip-test"))
        Prove(service, userId, parked);
    Console.WriteLine("Plugins registered.");
}
catch (Exception ex)
{
    exitCode = 1;
    Console.Error.WriteLine(Describe(ex));
    if (!string.IsNullOrWhiteSpace(service.LastError) && !Describe(ex).Contains(service.LastError, StringComparison.Ordinal))
        Console.Error.WriteLine(service.LastError);
}
finally
{
    RemoveTestData(service);
    foreach (var id in parked)
    {
        try
        {
            service.Update(new Entity("cicapos_assignmentrule", id) { ["cicapos_isavailable"] = true });
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("Could not restore assignment rule " + id + ": " + Describe(ex));
        }
    }
}
Environment.Exit(exitCode);
return exitCode;

static void DumpAiDiag(ServiceClient service)
{
    var sq = new QueryExpression("sdkmessageprocessingstep")
    {
        ColumnSet = new ColumnSet("name", "stage", "statecode", "impersonatinguserid", "plugintypeid"),
        TopCount = 20
    };
    sq.Criteria.AddCondition("name", ConditionOperator.Like, "%AiAssist%");
    foreach (var s in service.RetrieveMultiple(sq).Entities)
        Console.WriteLine("STEP " + s.GetAttributeValue<string>("name")
            + " stage=" + s.GetAttributeValue<OptionSetValue>("stage")?.Value
            + " state=" + s.GetAttributeValue<OptionSetValue>("statecode")?.Value
            + " impersonate=" + s.GetAttributeValue<EntityReference>("impersonatinguserid")?.Id);

    var defQ = new QueryExpression("environmentvariabledefinition")
    {
        ColumnSet = new ColumnSet("schemaname", "defaultvalue"),
        TopCount = 5
    };
    defQ.Criteria.AddCondition("schemaname", ConditionOperator.Equal, "cicapos_AiAssistConfig");
    var def = service.RetrieveMultiple(defQ).Entities.FirstOrDefault();
    if (def == null) Console.WriteLine("ENV def missing");
    else
    {
        var dv = def.GetAttributeValue<string>("defaultvalue") ?? "";
        Console.WriteLine("ENV def len=" + dv.Length + " hasAk=" + dv.Contains("ak-") + " hasSk=" + dv.Contains("sk-"));
        var valQ = new QueryExpression("environmentvariablevalue")
        {
            ColumnSet = new ColumnSet("value"),
            TopCount = 1
        };
        valQ.Criteria.AddCondition("environmentvariabledefinitionid", ConditionOperator.Equal, def.Id);
        var val = service.RetrieveMultiple(valQ).Entities.FirstOrDefault()?.GetAttributeValue<string>("value") ?? "";
        Console.WriteLine("ENV val len=" + val.Length + " hasAk=" + val.Contains("ak-") + " hasSk=" + val.Contains("sk-"));
    }

    try
    {
        var id = service.Create(new Entity("cicapos_aiassistjob")
        {
            ["cicapos_name"] = "diag " + DateTime.UtcNow.ToString("HH:mm:ss"),
            ["cicapos_action"] = "validate_upload",
            ["cicapos_schemakey"] = "supporting_doc",
            ["cicapos_doctype"] = "nar1",
            ["cicapos_filename"] = "probe.txt",
            ["cicapos_documenttext"] = "NAR1 sample text for CICAPOS diagnostic."
        });
        Console.WriteLine("CREATE OK " + id);
        var row = service.Retrieve("cicapos_aiassistjob", id, new ColumnSet("cicapos_source", "cicapos_notes", "cicapos_pass", "cicapos_model"));
        Console.WriteLine("source=" + row.GetAttributeValue<string>("cicapos_source")
            + " pass=" + row.GetAttributeValue<bool?>("cicapos_pass")
            + " model=" + row.GetAttributeValue<string>("cicapos_model")
            + " notes=" + row.GetAttributeValue<string>("cicapos_notes"));
        try { service.Delete("cicapos_aiassistjob", id); } catch { }
    }
    catch (Exception ex)
    {
        Console.WriteLine("CREATE FAIL " + Describe(ex));
    }

    DumpAiTraces(service);
}

static void DumpAiTraces(ServiceClient service)
{
    try
    {
        var q = new QueryExpression("plugintracelog")
        {
            ColumnSet = new ColumnSet("messagename", "exceptiondetails", "messageblock", "createdon", "typename"),
            TopCount = 8
        };
        q.Criteria.AddCondition("primaryentity", ConditionOperator.Equal, "cicapos_aiassistjob");
        q.Orders.Add(new OrderExpression("createdon", OrderType.Descending));
        var rows = service.RetrieveMultiple(q).Entities;
        Console.WriteLine("plugin traces " + rows.Count);
        foreach (var row in rows)
        {
            var ex = row.GetAttributeValue<string>("exceptiondetails") ?? "";
            var msg = row.GetAttributeValue<string>("messageblock") ?? "";
            if (ex.Length > 400) ex = ex[..400];
            if (msg.Length > 200) msg = msg[..200];
            Console.WriteLine(row.GetAttributeValue<DateTime>("createdon") + " " + row.GetAttributeValue<string>("typename")
                + " " + row.GetAttributeValue<string>("messagename") + "\n  " + ex + "\n  " + msg);
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine("plugin traces: " + Describe(ex).Split('|')[0].Trim());
    }
}

static Guid UploadAssembly(ServiceClient service, string dll, string name, string token, string version)
{
    var content = Convert.ToBase64String(File.ReadAllBytes(dll));
    var query = new QueryExpression("pluginassembly") { ColumnSet = new ColumnSet("pluginassemblyid"), TopCount = 1 };
    query.Criteria.AddCondition("name", ConditionOperator.Equal, name);
    var existing = service.RetrieveMultiple(query).Entities.FirstOrDefault();
    if (existing == null)
    {
        var created = new Entity("pluginassembly")
        {
            ["name"] = name,
            ["content"] = content,
            ["isolationmode"] = new OptionSetValue(2),
            ["sourcetype"] = new OptionSetValue(0),
            ["culture"] = "neutral",
            ["publickeytoken"] = token,
            ["version"] = version,
            ["description"] = "CIC APOS funding route, assignment, and disbursement checks."
        };
        var id = service.Create(created);
        Console.WriteLine("assembly created " + id);
        return id;
    }
    service.Update(new Entity("pluginassembly", existing.Id)
    {
        ["content"] = content,
        ["culture"] = "neutral",
        ["publickeytoken"] = token,
        ["version"] = version
    });
    Console.WriteLine("assembly updated " + existing.Id);
    return existing.Id;
}

static Guid EnsureType(ServiceClient service, Guid assemblyId, string typeName)
{
    var query = new QueryExpression("plugintype") { ColumnSet = new ColumnSet("plugintypeid", "pluginassemblyid"), TopCount = 1 };
    query.Criteria.AddCondition("typename", ConditionOperator.Equal, typeName);
    var existing = service.RetrieveMultiple(query).Entities.FirstOrDefault();
    if (existing != null)
    {
        if (existing.GetAttributeValue<EntityReference>("pluginassemblyid")?.Id != assemblyId)
            service.Update(new Entity("plugintype", existing.Id) { ["pluginassemblyid"] = new EntityReference("pluginassembly", assemblyId) });
        Console.WriteLine("type " + typeName);
        return existing.Id;
    }
    var id = service.Create(new Entity("plugintype")
    {
        ["pluginassemblyid"] = new EntityReference("pluginassembly", assemblyId),
        ["typename"] = typeName,
        ["name"] = typeName,
        ["friendlyname"] = typeName
    });
    Console.WriteLine("type created " + typeName);
    return id;
}

static Guid EnsureStep(ServiceClient service, Guid typeId, Guid userId, string typeName, string entity, string message, int stage, int rank)
{
    var (filterId, messageId) = Filter(service, entity, message);
    var name = $"{typeName}: {message} of {entity} ({stage})";
    var query = new QueryExpression("sdkmessageprocessingstep") { ColumnSet = new ColumnSet("sdkmessageprocessingstepid"), TopCount = 1 };
    query.Criteria.AddCondition("name", ConditionOperator.Equal, name);
    var existing = service.RetrieveMultiple(query).Entities.FirstOrDefault();
    var step = existing ?? new Entity("sdkmessageprocessingstep");
    step["name"] = name;
    step["description"] = "CIC APOS";
    step["plugintypeid"] = new EntityReference("plugintype", typeId);
    step["sdkmessageid"] = new EntityReference("sdkmessage", messageId);
    step["sdkmessagefilterid"] = new EntityReference("sdkmessagefilter", filterId);
    step["mode"] = new OptionSetValue(0);
    step["stage"] = new OptionSetValue(stage);
    step["rank"] = rank;
    step["supporteddeployment"] = new OptionSetValue(0);
    step["asyncautodelete"] = false;
    step["impersonatinguserid"] = new EntityReference("systemuser", userId);
    if (existing == null)
    {
        var id = service.Create(step);
        Console.WriteLine("step " + name);
        return id;
    }
    service.Update(step);
    Console.WriteLine("step updated " + name);
    return existing.Id;
}

static (Guid Filter, Guid Message) Filter(ServiceClient service, string entity, string message)
{
    var messageQuery = new QueryExpression("sdkmessage") { ColumnSet = new ColumnSet("sdkmessageid"), TopCount = 1 };
    messageQuery.Criteria.AddCondition("name", ConditionOperator.Equal, message);
    var messageId = service.RetrieveMultiple(messageQuery).Entities.First().Id;
    var metadata = ((RetrieveEntityResponse)service.Execute(new RetrieveEntityRequest { LogicalName = entity, EntityFilters = EntityFilters.Entity })).EntityMetadata;
    foreach (var key in new[] { entity, metadata.ObjectTypeCode?.ToString() ?? "" })
    {
        if (key.Length == 0) continue;
        var query = new QueryExpression("sdkmessagefilter")
        {
            ColumnSet = new ColumnSet("sdkmessagefilterid", "iscustomprocessingstepallowed"),
            TopCount = 10
        };
        query.Criteria.AddCondition("sdkmessageid", ConditionOperator.Equal, messageId);
        query.Criteria.AddCondition("primaryobjecttypecode", ConditionOperator.Equal, key);
        var rows = service.RetrieveMultiple(query).Entities;
        var row = rows.FirstOrDefault(candidate => candidate.GetAttributeValue<bool>("iscustomprocessingstepallowed")) ?? rows.FirstOrDefault();
        if (row != null) return (row.Id, messageId);
    }
    throw new InvalidOperationException($"No SDK message filter for {message} of {entity}.");
}

static void AddComponent(ServiceClient service, Guid id, int componentType, bool required)
{
    try
    {
        service.Execute(new AddSolutionComponentRequest
        {
            ComponentId = id,
            ComponentType = componentType,
            SolutionUniqueName = Solution,
            AddRequiredComponents = required
        });
        Console.WriteLine($"solution {componentType} {id}");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"solution {componentType} {id} not added: {Describe(ex).Split('|')[0].Trim()}");
    }
}

static void SeedRules(ServiceClient service)
{
    var seeds = new (string Key, decimal Value, string Name, string Unit)[]
    {
        ("actBimCap", 7_500_000m, "ACT and BIM combined cap", "HKD"),
        ("quotesPreapproved", 1m, "Quotations for a pre-approved item", "count"),
        ("quotesNpa", 3m, "Quotations for an NPA item", "count"),
        ("firstInstalmentPercent", 80m, "First instalment percent", "percent"),
        ("finalWaitMonths", 12m, "Months before the final payment", "months"),
        ("spotCheckEvery", 10m, "Disbursements between spot checks", "count")
    };
    var query = new QueryExpression("cicapos_fundingrule") { ColumnSet = new ColumnSet("cicapos_key") };
    query.Criteria.AddCondition("cicapos_key", ConditionOperator.In, seeds.Select(seed => (object)seed.Key).ToArray());
    var present = service.RetrieveMultiple(query).Entities
        .Select(row => row.GetAttributeValue<string>("cicapos_key") ?? "")
        .ToHashSet(StringComparer.OrdinalIgnoreCase);
    foreach (var seed in seeds)
    {
        if (present.Contains(seed.Key))
        {
            Console.WriteLine("rule exists " + seed.Key);
            continue;
        }
        service.Create(new Entity("cicapos_fundingrule")
        {
            ["cicapos_name"] = seed.Name,
            ["cicapos_key"] = seed.Key,
            ["cicapos_value"] = seed.Value,
            ["cicapos_unit"] = seed.Unit,
            ["cicapos_description"] = seed.Name
        });
        Console.WriteLine("rule created " + seed.Key);
    }
}

static void Prove(ServiceClient service, Guid userId, List<Guid> parked)
{
    RemoveTestData(service);
    var available = new QueryExpression("cicapos_assignmentrule") { ColumnSet = new ColumnSet("cicapos_assignmentruleid") };
    available.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0);
    available.Criteria.AddCondition("cicapos_isavailable", ConditionOperator.Equal, true);
    foreach (var row in service.RetrieveMultiple(available).Entities)
    {
        service.Update(new Entity(row.LogicalName, row.Id) { ["cicapos_isavailable"] = false });
        parked.Add(row.Id);
    }
    foreach (var process in new[] { 542400000, 542400001, 542400002 })
    {
        service.Create(new Entity("cicapos_assignmentrule")
        {
            ["cicapos_name"] = Marker + " " + process,
            ["cicapos_process"] = new OptionSetValue(process),
            ["cicapos_isavailable"] = true,
            ["cicapos_officerid"] = new EntityReference("systemuser", userId)
        });
    }

    var schemes = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
    var schemeQuery = new QueryExpression("cicapos_fundingscheme") { ColumnSet = new ColumnSet("cicapos_code") };
    schemeQuery.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0);
    foreach (var row in service.RetrieveMultiple(schemeQuery).Entities)
    {
        var code = Rules.Normalize(row.GetAttributeValue<string>("cicapos_code"));
        if (code.Length > 0) schemes[code] = row.Id;
    }
    Console.WriteLine("schemes " + string.Join(", ", schemes.Keys.OrderBy(code => code)));
    var act = schemes["ACT"];
    var cap = RuleValue(service, "actBimCap", 7_500_000m);

    var account = service.Create(new Entity("account") { ["name"] = Marker });
    var capped = Application(service, account);
    Item(service, capped, act, Rules.PreapprovedItem, cap + 1m, true, null, 1);
    ExpectRejected(service, "ACT and BIM cap", "exceeds the cap", () => Submit(service, capped));

    var application = Application(service, account);
    var item = Item(service, application, act, Rules.PreapprovedItem, 100m, true, null, 9);
    var savedItem = service.Retrieve("cicapos_fundingitem", item, new ColumnSet("cicapos_quotesrequired"));
    Check("pre-approved item requires one quotation", savedItem.GetAttributeValue<int>("cicapos_quotesrequired") == 1, "quotesrequired=" + savedItem.GetAttributeValue<int>("cicapos_quotesrequired"));
    Submit(service, application);
    var submitted = service.Retrieve("cicapos_fundingapplication", application, new ColumnSet("cicapos_routetype", "cicapos_status", "cicapos_casehandlerid", "ownerid"));
    Check("short route", Option(submitted, "cicapos_routetype") == Rules.ShortRoute, "route=" + Option(submitted, "cicapos_routetype"));
    Check("assigned to the available officer", Option(submitted, "cicapos_status") == Rules.Assigned && submitted.GetAttributeValue<EntityReference>("cicapos_casehandlerid")?.Id == userId && submitted.GetAttributeValue<EntityReference>("ownerid")?.Id == userId, "status=" + Option(submitted, "cicapos_status"));

    service.Update(new Entity("cicapos_fundingapplication", application) { ["cicapos_routetype"] = new OptionSetValue(Rules.LongRoute) });
    var locked = service.Retrieve("cicapos_fundingapplication", application, new ColumnSet("cicapos_routetype"));
    Check("applicant cannot change the route", Option(locked, "cicapos_routetype") == Rules.ShortRoute, "route=" + Option(locked, "cicapos_routetype"));

    ExpectRejected(service, "missing justification", "justification", () => Item(service, application, act, Rules.NewNpa, 100m, false, null, 1));
    var npa = Item(service, application, act, Rules.NewNpa, 100m, false, "Only one supplier can meet the specification.", 1);
    var npaRow = service.Retrieve("cicapos_fundingitem", npa, new ColumnSet("cicapos_quotesrequired"));
    Check("new NPA requires three quotations", npaRow.GetAttributeValue<int>("cicapos_quotesrequired") == 3, "quotesrequired=" + npaRow.GetAttributeValue<int>("cicapos_quotesrequired"));
    var rerouted = service.Retrieve("cicapos_fundingapplication", application, new ColumnSet("cicapos_routetype"));
    Check("new NPA uses the long route", Option(rerouted, "cicapos_routetype") == Rules.LongRoute, "route=" + Option(rerouted, "cicapos_routetype"));

    ExpectRejected(service, "disbursement before approval", "approved funding application", () => Disbursement(service, application, 80m, Rules.FirstInstalment, true));
    service.Update(new Entity("cicapos_fundingapplication", application)
    {
        ["cicapos_status"] = new OptionSetValue(Rules.FundingApproved),
        ["cicapos_approvedgrant"] = 100m
    });
    ExpectRejected(service, "first instalment cap", "first instalment", () => Disbursement(service, application, 90m, Rules.FirstInstalment, false));
    var request = Disbursement(service, application, 80m, Rules.FirstInstalment, true);
    var requestRow = service.Retrieve("cicapos_disbursementrequest", request, new ColumnSet("cicapos_name", "cicapos_reviewrequired", "cicapos_spotcheckselected", "cicapos_casehandlerid"));
    var reference = requestRow.GetAttributeValue<string>("cicapos_name");
    var expectedSpot = Rules.SelectedForSpotCheck(reference, (int)RuleValue(service, "spotCheckEvery", 10m));
    Check("ACT disbursement is not sent to review", requestRow.GetAttributeValue<bool>("cicapos_reviewrequired") == false, reference);
    Check("spot check follows the reference", requestRow.GetAttributeValue<bool>("cicapos_spotcheckselected") == expectedSpot, reference + " selected=" + requestRow.GetAttributeValue<bool>("cicapos_spotcheckselected"));
    Check("disbursement is assigned", requestRow.GetAttributeValue<EntityReference>("cicapos_casehandlerid")?.Id == userId, "handler missing");
    service.Update(new Entity("cicapos_disbursementrequest", request)
    {
        ["cicapos_reviewrequired"] = true,
        ["cicapos_spotcheckselected"] = !expectedSpot
    });
    var forced = service.Retrieve("cicapos_disbursementrequest", request, new ColumnSet("cicapos_reviewrequired", "cicapos_spotcheckselected"));
    Check("applicant cannot change review or spot check", forced.GetAttributeValue<bool>("cicapos_reviewrequired") == false && forced.GetAttributeValue<bool>("cicapos_spotcheckselected") == expectedSpot, reference);
    ExpectRejected(service, "final payment wait", "final payment", () => Disbursement(service, application, 10m, Rules.FinalInstalment, false));

    if (!schemes.TryGetValue("MIC", out var mic))
    {
        Console.WriteLine("SKIP MiC review — scheme MIC is not present");
        return;
    }
    var micApplication = Application(service, account);
    Item(service, micApplication, mic, Rules.PreapprovedItem, 100m, true, null, 1);
    service.Update(new Entity("cicapos_fundingapplication", micApplication)
    {
        ["cicapos_status"] = new OptionSetValue(Rules.FundingApproved),
        ["cicapos_approvedgrant"] = 100m
    });
    var micRequest = Disbursement(service, micApplication, 10m, Rules.FirstInstalment, false);
    var micRow = service.Retrieve("cicapos_disbursementrequest", micRequest, new ColumnSet("cicapos_reviewrequired", "cicapos_spotcheckselected"));
    Check("MiC disbursement requires review and skips the spot check", micRow.GetAttributeValue<bool>("cicapos_reviewrequired") && micRow.GetAttributeValue<bool>("cicapos_spotcheckselected") == false, "review=" + micRow.GetAttributeValue<bool>("cicapos_reviewrequired"));
}

static Guid Application(ServiceClient service, Guid account)
    => service.Create(new Entity("cicapos_fundingapplication")
    {
        ["cicapos_projectname"] = Marker,
        ["cicapos_applicantid"] = new EntityReference("account", account),
        ["cicapos_status"] = new OptionSetValue(Rules.Draft),
        ["cicapos_pledgedate"] = DateTime.UtcNow.Date.AddDays(90)
    });

static Guid Item(ServiceClient service, Guid application, Guid scheme, int itemType, decimal price, bool lowest, string? justification, int quotes)
{
    var entity = new Entity("cicapos_fundingitem")
    {
        ["cicapos_name"] = Marker + " item",
        ["cicapos_fundingapplicationid"] = new EntityReference("cicapos_fundingapplication", application),
        ["cicapos_schemeid"] = new EntityReference("cicapos_fundingscheme", scheme),
        ["cicapos_itemtype"] = new OptionSetValue(itemType),
        ["cicapos_quantity"] = 1m,
        ["cicapos_unitprice"] = price,
        ["cicapos_islowestquote"] = lowest,
        ["cicapos_quotesrequired"] = quotes
    };
    if (justification != null) entity["cicapos_justification"] = justification;
    return service.Create(entity);
}

static Guid Disbursement(ServiceClient service, Guid application, decimal claimed, int instalment, bool flags)
{
    var entity = new Entity("cicapos_disbursementrequest")
    {
        ["cicapos_fundingapplicationid"] = new EntityReference("cicapos_fundingapplication", application),
        ["cicapos_instalmenttype"] = new OptionSetValue(instalment),
        ["cicapos_claimedamount"] = claimed,
        ["cicapos_status"] = new OptionSetValue(Rules.Submitted)
    };
    if (flags)
    {
        entity["cicapos_reviewrequired"] = true;
        entity["cicapos_spotcheckselected"] = true;
    }
    return service.Create(entity);
}

static void Submit(ServiceClient service, Guid application)
    => service.Update(new Entity("cicapos_fundingapplication", application) { ["cicapos_status"] = new OptionSetValue(Rules.Submitted) });

static decimal RuleValue(ServiceClient service, string key, decimal fallback)
{
    var query = new QueryExpression("cicapos_fundingrule") { ColumnSet = new ColumnSet("cicapos_value"), TopCount = 1 };
    query.Criteria.AddCondition("cicapos_key", ConditionOperator.Equal, key);
    query.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0);
    var row = service.RetrieveMultiple(query).Entities.FirstOrDefault();
    var value = row?.GetAttributeValue<decimal>("cicapos_value") ?? 0m;
    return value > 0 ? value : fallback;
}

static int Option(Entity entity, string attribute) => entity.GetAttributeValue<OptionSetValue>(attribute)?.Value ?? -1;

static void Check(string label, bool ok, string detail)
{
    if (!ok) throw new InvalidOperationException(label + " — " + detail);
    Console.WriteLine("PASS " + label);
}

static void ExpectRejected(ServiceClient service, string label, string fragment, Action action)
{
    try
    {
        action();
    }
    catch (Exception ex)
    {
        var text = Describe(ex) + " " + service.LastError;
        if (text.Contains(fragment, StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine("PASS " + label);
            return;
        }
        throw new InvalidOperationException(label + " failed for a different reason: " + text, ex);
    }
    throw new InvalidOperationException(label + " was saved.");
}

static void RemoveTestData(ServiceClient service)
{
    foreach (var application in Ids(service, "cicapos_fundingapplication", "cicapos_projectname", Marker, ConditionOperator.Equal))
    {
        foreach (var request in Ids(service, "cicapos_disbursementrequest", "cicapos_fundingapplicationid", application, ConditionOperator.Equal))
        {
            foreach (var line in Ids(service, "cicapos_disbursementline", "cicapos_disbursementrequestid", request, ConditionOperator.Equal))
                TryDelete(service, "cicapos_disbursementline", line);
            foreach (var process in Ids(service, "cicapos_bpf_disbursement", "bpf_cicapos_disbursementrequestid", request, ConditionOperator.Equal))
                TryDelete(service, "cicapos_bpf_disbursement", process);
            TryDelete(service, "cicapos_disbursementrequest", request);
        }
        foreach (var item in Ids(service, "cicapos_fundingitem", "cicapos_fundingapplicationid", application, ConditionOperator.Equal))
            TryDelete(service, "cicapos_fundingitem", item);
        foreach (var process in Ids(service, "cicapos_bpf_preapproved", "bpf_cicapos_fundingapplicationid", application, ConditionOperator.Equal))
            TryDelete(service, "cicapos_bpf_preapproved", process);
        foreach (var process in Ids(service, "cicapos_bpf_npa", "bpf_cicapos_fundingapplicationid", application, ConditionOperator.Equal))
            TryDelete(service, "cicapos_bpf_npa", process);
        TryDelete(service, "cicapos_fundingapplication", application);
    }
    foreach (var account in Ids(service, "account", "name", Marker, ConditionOperator.Equal))
        TryDelete(service, "account", account);
    foreach (var rule in Ids(service, "cicapos_assignmentrule", "cicapos_name", Marker, ConditionOperator.BeginsWith))
        TryDelete(service, "cicapos_assignmentrule", rule);
}

static List<Guid> Ids(ServiceClient service, string entity, string attribute, object value, ConditionOperator op)
{
    var query = new QueryExpression(entity) { ColumnSet = new ColumnSet(false) };
    query.Criteria.AddCondition(attribute, op, value);
    query.PageInfo = new PagingInfo { Count = 500, PageNumber = 1 };
    var ids = new List<Guid>();
    while (true)
    {
        var page = service.RetrieveMultiple(query);
        ids.AddRange(page.Entities.Select(row => row.Id));
        if (!page.MoreRecords) return ids;
        query.PageInfo.PageNumber++;
        query.PageInfo.PagingCookie = page.PagingCookie;
    }
}

static void TryDelete(ServiceClient service, string entity, Guid id)
{
    try
    {
        service.Delete(entity, id);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"delete {entity} failed: {Describe(ex)}");
    }
}

static string Describe(Exception exception)
{
    var parts = new List<string>();
    for (var current = exception; current != null; current = current.InnerException)
        parts.Add(current.Message);
    if (exception is FaultException<OrganizationServiceFault> fault)
    {
        for (var inner = fault.Detail; inner != null; inner = inner.InnerFault)
            if (!string.IsNullOrWhiteSpace(inner.Message)) parts.Add(inner.Message);
    }
    return string.Join(" | ", parts.Where(part => !string.IsNullOrWhiteSpace(part)).Distinct());
}
