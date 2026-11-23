using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;

static class PortalSetup
{
    const string Site = "ea8078b2-2728-4878-9694-7992c3061c1b";
    const string Language = "54124646-8ab8-40d3-bb55-bcdf19b50545";
    const string PublishingState = "11327ec1-d362-4ca8-b603-20f7ed715821";
    const string PageTemplate = "0005c373-72eb-4494-8096-b7887c1d7c54";
    const string HomeRoot = "223204cd-dc77-4840-afd1-334ccce38916";
    const string HomeCopy = "165c3e75-3900-436f-a522-7be187441c9e";
    const string LinkSet = "f36f02b0-c970-48e7-a5a1-84527f84ae50";
    const string CompanyName = "Harbour Construction Limited";
    const string ContactEmail = "sam.lee@harbour-construction.example";

    const string ActiveFundingView = "f9b8a9ad-8809-4fb3-a757-8b92c3b31edf";
    const string ActiveDisbursementView = "903d547a-802f-43a1-9e1c-b7c22b732c5a";
    const string ActivePalView = "cd0f3d6b-bd79-470e-8321-6b13505208c5";
    const string ActiveItemView = "41337f08-2bb6-4298-ada1-e51492d383aa";

    const string MyApplicationsRoot = "a5100001-e2e1-4111-8111-6045bd591001";
    const string MyApplicationsCopy = "a5100001-e2e1-4111-8111-6045bd591002";
    const string FundingRoot = "a5100001-e2e1-4111-8111-6045bd591003";
    const string FundingCopy = "a5100001-e2e1-4111-8111-6045bd591004";
    const string DisbursementRoot = "a5100001-e2e1-4111-8111-6045bd591005";
    const string DisbursementCopy = "a5100001-e2e1-4111-8111-6045bd591006";
    const string PalRoot = "a5100001-e2e1-4111-8111-6045bd591007";
    const string PalCopy = "a5100001-e2e1-4111-8111-6045bd591008";

    const string NewFundingForm = "a5100001-e2e1-4111-8111-6045bd591010";
    const string EditFundingForm = "a5100001-e2e1-4111-8111-6045bd591011";
    const string SubmitFundingForm = "a5100001-e2e1-4111-8111-6045bd591012";
    const string ItemForm = "a5100001-e2e1-4111-8111-6045bd591013";
    const string DisbursementForm = "a5100001-e2e1-4111-8111-6045bd591014";
    const string PalForm = "a5100001-e2e1-4111-8111-6045bd591015";

    const string FundingList = "a5100001-e2e1-4111-8111-6045bd591020";
    const string DisbursementList = "a5100001-e2e1-4111-8111-6045bd591021";
    const string PalList = "a5100001-e2e1-4111-8111-6045bd591022";

    public static void Run(ServiceClient service, Guid userId)
    {
        var types = ComponentTypes(service);
        var pageType = types.Require("Web Page", "网页", "網頁");
        var linkType = types.Require("Web Link", "网页链接", "網頁連結");
        var formType = types.Require("Basic Form", "Entity Form", "基本窗体", "基本表單");
        var listType = types.Require("List", "Entity List", "列表");
        var metadataType = types.Require("Basic Form Metadata", "Entity Form Metadata", "基本窗体元数据", "基本表單元資料");
        var solutionType = SolutionComponentType(service, Guid.Parse(HomeRoot));

        EnsurePortalForm(service, "cicapos_fundingapplication", "Portal", FundingApplicationForm());
        EnsurePortalForm(service, "cicapos_fundingapplication", "Portal Submit", SubmitForm());
        EnsurePortalForm(service, "cicapos_fundingitem", "Portal", FundingItemForm());
        EnsurePortalForm(service, "cicapos_disbursementrequest", "Portal", DisbursementFormXml());
        EnsurePortalForm(service, "cicapos_palapplication", "Portal", PalFormXml());
        service.Execute(new PublishXmlRequest
        {
            ParameterXml = "<importexportxml><entities><entity>cicapos_fundingapplication</entity><entity>cicapos_fundingitem</entity><entity>cicapos_disbursementrequest</entity><entity>cicapos_palapplication</entity></entities></importexportxml>"
        });
        Console.WriteLine("portal forms published");

        var pages = new (string Root, string Copy, string Title, string Url, int Order, string Html)[]
        {
            (MyApplicationsRoot, MyApplicationsCopy, "My Applications", "my-applications", 2, MyApplicationsHtml()),
            (FundingRoot, FundingCopy, "Funding Application", "funding-application", 3, FundingHtml()),
            (DisbursementRoot, DisbursementCopy, "Disbursement Request", "disbursement-request", 4, DisbursementHtml()),
            (PalRoot, PalCopy, "Pre-approved List Application", "pre-approved-list", 5, PalHtml())
        };
        var linkIds = new[]
        {
            "a5100001-e2e1-4111-8111-6045bd591040",
            "a5100001-e2e1-4111-8111-6045bd591041",
            "a5100001-e2e1-4111-8111-6045bd591042",
            "a5100001-e2e1-4111-8111-6045bd591043"
        };
        for (var index = 0; index < pages.Length; index++)
        {
            var page = pages[index];
            Upsert(service, page.Root, page.Title, pageType, null, PageContent(page.Title, page.Url, page.Order, HomeRoot, null, null), solutionType);
            Upsert(service, page.Copy, page.Title, pageType, Guid.Parse(Language), PageContent(page.Title, page.Url, page.Order, null, page.Root, page.Html), solutionType);
            Upsert(service, linkIds[index], page.Title, linkType, null, LinkContent(page.Order, page.Root), solutionType);
        }

        Upsert(service, NewFundingForm, "New Funding Application", formType, null, BasicForm("cicapos_fundingapplication", "Portal", 100000000, "Draft saved. Add the funding items next.", FundingRoot, true), solutionType);
        Upsert(service, EditFundingForm, "Funding Application", formType, null, BasicForm("cicapos_fundingapplication", "Portal", 100000001, "Funding application saved.", null, false), solutionType);
        Upsert(service, SubmitFundingForm, "Submit Funding Application", formType, null, BasicForm("cicapos_fundingapplication", "Portal Submit", 100000001, "Funding application submitted.", MyApplicationsRoot, false, "Submit"), solutionType);
        Upsert(service, ItemForm, "Funding Item", formType, null, ItemBasicForm(), solutionType);
        Upsert(service, DisbursementForm, "New Disbursement Request", formType, null, BasicForm("cicapos_disbursementrequest", "Portal", 100000000, "Disbursement request submitted.", MyApplicationsRoot, false, "Submit"), solutionType);
        Upsert(service, PalForm, "New Pre-approved List Application", formType, null, BasicForm("cicapos_palapplication", "Portal", 100000000, "Pre-approved list application submitted.", MyApplicationsRoot, false, "Submit"), solutionType);

        Upsert(service, FundingList, "My Funding Applications", listType, null, List("cicapos_fundingapplication", ActiveFundingView, "No funding applications yet.", FundingRoot), solutionType);
        Upsert(service, DisbursementList, "My Disbursement Requests", listType, null, List("cicapos_disbursementrequest", ActiveDisbursementView, "No disbursement requests yet.", null), solutionType);
        Upsert(service, PalList, "My Pre-approved List Applications", listType, null, List("cicapos_palapplication", ActivePalView, "No pre-approved list applications yet.", null), solutionType);

        Metadata(service, metadataType, solutionType, "a5100001-e2e1-4111-8111-6045bd591030", NewFundingForm, "cicapos_status", "542400000");
        Metadata(service, metadataType, solutionType, "a5100001-e2e1-4111-8111-6045bd591031", NewFundingForm, "cicapos_applicantid", null, "parentcustomerid");
        Metadata(service, metadataType, solutionType, "a5100001-e2e1-4111-8111-6045bd591032", NewFundingForm, "cicapos_contactid", null, null);
        Metadata(service, metadataType, solutionType, "a5100001-e2e1-4111-8111-6045bd591033", EditFundingForm, "cicapos_applicantid", null, "parentcustomerid");
        Metadata(service, metadataType, solutionType, "a5100001-e2e1-4111-8111-6045bd591034", EditFundingForm, "cicapos_contactid", null, null);
        Metadata(service, metadataType, solutionType, "a5100001-e2e1-4111-8111-6045bd591035", SubmitFundingForm, "cicapos_status", "542400001");
        Metadata(service, metadataType, solutionType, "a5100001-e2e1-4111-8111-6045bd591036", DisbursementForm, "cicapos_status", "542400001");
        Metadata(service, metadataType, solutionType, "a5100001-e2e1-4111-8111-6045bd591037", PalForm, "cicapos_status", "542400001");
        Metadata(service, metadataType, solutionType, "a5100001-e2e1-4111-8111-6045bd591038", PalForm, "cicapos_applicantid", null, "parentcustomerid");

        UpdateHome(service);
        Seed(service, userId);
        Console.WriteLine("portal pages ready");
    }

    static void Seed(ServiceClient service, Guid userId)
    {
        foreach (var process in new[] { (542400000, "Funding Application"), (542400001, "Disbursement Request"), (542400002, "Pre-approved List") })
        {
            var existing = new QueryExpression("cicapos_assignmentrule") { ColumnSet = new ColumnSet("cicapos_assignmentruleid"), TopCount = 1 };
            existing.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0);
            existing.Criteria.AddCondition("cicapos_isavailable", ConditionOperator.Equal, true);
            existing.Criteria.AddCondition("cicapos_process", ConditionOperator.Equal, process.Item1);
            if (service.RetrieveMultiple(existing).Entities.Count == 0)
            {
                service.Create(new Entity("cicapos_assignmentrule")
                {
                    ["cicapos_name"] = "Default — " + process.Item2,
                    ["cicapos_process"] = new OptionSetValue(process.Item1),
                    ["cicapos_isavailable"] = true,
                    ["cicapos_officerid"] = new EntityReference("systemuser", userId)
                });
                Console.WriteLine("assignment rule Default — " + process.Item2);
            }
        }

        var account = FindOrCreate(service, "account", "name", CompanyName, () => new Entity("account") { ["name"] = CompanyName });
        var contact = FindOrCreate(service, "contact", "emailaddress1", ContactEmail, () => new Entity("contact")
        {
            ["firstname"] = "Sam",
            ["lastname"] = "Lee",
            ["emailaddress1"] = ContactEmail,
            ["parentcustomerid"] = new EntityReference("account", account)
        });
        service.Update(new Entity("contact", contact) { ["parentcustomerid"] = new EntityReference("account", account) });

        var schemes = Schemes(service);
        var pledge = DateTime.UtcNow.Date.AddDays(120);
        var draft = EnsureApplication(service, account, contact, "Harbour Tower BIM coordination",
            "Coordinate the BIM model for the tower core.", pledge, null);
        EnsureItem(service, draft, schemes["BIM"], 542400000, 1m, 250_000m, true, null, "BIM coordination");

        var act = EnsureApplication(service, account, contact, "Site office ACT equipment",
            "Buy the approved site-office equipment.", pledge, 200_000m);
        EnsureItem(service, act, schemes["ACT"], 542400000, 1m, 200_000m, true, null, "ACT equipment");
        SubmitThenApprove(service, act, 200_000m);
        EnsureDisbursement(service, act, 100_000m, "first");

        var mic = EnsureApplication(service, account, contact, "Modular bathroom MiC",
            "Install the modular bathroom units.", pledge, 500_000m);
        EnsureItem(service, mic, schemes["MIC"], 542400000, 1m, 500_000m, true, null, "MiC bathroom");
        SubmitThenApprove(service, mic, 500_000m);
        EnsureDisbursement(service, mic, 200_000m, "MiC");

        var npa = EnsureApplication(service, account, contact, "New scanning workflow",
            "Try a scanning method that is not on the pre-approved list.", pledge, null);
        EnsureItem(service, npa, schemes["STB"], 542400002, 1m, 80_000m, false, "Only one supplier can capture this site.", "New scanning");
        EnsureSubmitted(service, npa);

        EnsurePal(service, account, schemes["STB"], "Scaffold sensor", 542400000, 18_000m, "Draft listing for a scaffold sensor.", false);
        EnsurePal(service, account, schemes["BIM"], "Drone survey kit", 542400000, 42_000m, "Request a listing for the survey kit.", true);
        Console.WriteLine("sample company " + CompanyName);
    }

    static Dictionary<string, int> ComponentTypes(ServiceClient service)
    {
        var response = (RetrieveAttributeResponse)service.Execute(new RetrieveAttributeRequest
        {
            EntityLogicalName = "powerpagecomponent",
            LogicalName = "powerpagecomponenttype",
            RetrieveAsIfPublished = true
        });
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var option in ((EnumAttributeMetadata)response.AttributeMetadata).OptionSet.Options)
        {
            var label = option.Label?.UserLocalizedLabel?.Label;
            if (label != null && option.Value != null)
            {
                map[label] = option.Value.Value;
                Console.WriteLine($"component {option.Value} {label}");
            }
        }
        return map;
    }

    static int Require(this Dictionary<string, int> types, params string[] names)
    {
        foreach (var name in names)
            if (types.TryGetValue(name, out var value))
                return value;
        throw new InvalidOperationException("No portal component type named " + string.Join(" / ", names));
    }

    static int SolutionComponentType(ServiceClient service, Guid objectId)
    {
        var query = new QueryExpression("solutioncomponent") { ColumnSet = new ColumnSet("componenttype"), TopCount = 1 };
        query.Criteria.AddCondition("objectid", ConditionOperator.Equal, objectId);
        return query.First(service).GetAttributeValue<OptionSetValue>("componenttype").Value;
    }

    static void EnsurePortalForm(ServiceClient service, string entity, string name, string formXml)
    {
        var query = new QueryExpression("systemform") { ColumnSet = new ColumnSet("formid"), TopCount = 1 };
        query.Criteria.AddCondition("name", ConditionOperator.Equal, name);
        query.Criteria.AddCondition("objecttypecode", ConditionOperator.Equal, entity);
        if (service.RetrieveMultiple(query).Entities.Count > 0)
        {
            Console.WriteLine($"form exists {entity} {name}");
            return;
        }
        var id = service.Create(new Entity("systemform")
        {
            ["name"] = name,
            ["description"] = "Applicant fields on CIC Portal App",
            ["type"] = new OptionSetValue(2),
            ["objecttypecode"] = entity,
            ["formxml"] = formXml
        });
        try
        {
            service.Execute(new AddSolutionComponentRequest { ComponentId = id, ComponentType = 60, SolutionUniqueName = "CICAPOS", AddRequiredComponents = false });
        }
        catch (Exception ex)
        {
            Console.WriteLine("form solution add: " + ex.Message);
        }
        Console.WriteLine($"form {entity} {name}");
    }

    static void Upsert(ServiceClient service, string id, string name, int type, Guid? language, string content, int solutionType)
    {
        var key = Guid.Parse(id);
        Entity? existing = null;
        try
        {
            existing = service.Retrieve("powerpagecomponent", key, new ColumnSet("powerpagecomponentid"));
        }
        catch
        {
            existing = null;
        }
        var entity = existing ?? new Entity("powerpagecomponent", key);
        entity["name"] = name;
        entity["powerpagecomponenttype"] = new OptionSetValue(type);
        entity["powerpagesiteid"] = new EntityReference("powerpagesite", Guid.Parse(Site));
        entity["content"] = content;
        if (language != null)
            entity["powerpagesitelanguageid"] = new EntityReference("powerpagesitelanguage", language.Value);
        if (existing == null)
            service.Create(entity);
        else
            service.Update(entity);
        try
        {
            service.Execute(new AddSolutionComponentRequest { ComponentId = key, ComponentType = solutionType, SolutionUniqueName = "CICAPOS", AddRequiredComponents = false });
        }
        catch (Exception ex) when (ex.Message.Contains("already", StringComparison.OrdinalIgnoreCase))
        {
        }
        Console.WriteLine($"portal {name}");
    }

    static void Metadata(ServiceClient service, int type, int solutionType, string id, string formId, string attribute, string? value, string? fromAttribute = null)
    {
        var content = new Dictionary<string, object?>
        {
            ["entityformid"] = formId,
            ["type"] = 100000000,
            ["attributelogicalname"] = attribute,
            ["setvalueonsave"] = true,
            ["onsavetype"] = value == null ? 100000002 : 100000000,
            ["description"] = attribute
        };
        if (value != null) content["onsavevalue"] = value;
        if (fromAttribute != null) content["onsavefromattribute"] = fromAttribute;
        Upsert(service, id, attribute + " on " + formId[..8], type, null, Json(content), solutionType);
    }

    static void UpdateHome(ServiceClient service)
    {
        var page = service.Retrieve("powerpagecomponent", Guid.Parse(HomeCopy), new ColumnSet("content"));
        var node = JsonNode.Parse(page.GetAttributeValue<string>("content"))!.AsObject();
        node["copy"] = """
            <div class="container" style="padding:32px 0;max-width:960px;">
              <h1>CIC Portal App</h1>
              <p>Submit a funding application, a disbursement request, or a pre-approved list application for your company.</p>
              <p><a class="btn btn-primary" href="/my-applications/">My Applications</a></p>
              <ul>
                <li><a href="/funding-application/">Funding Application</a></li>
                <li><a href="/disbursement-request/">Disbursement Request</a></li>
                <li><a href="/pre-approved-list/">Pre-approved List Application</a></li>
              </ul>
            </div>
            """;
        node["summary"] = "<p>Submit and track CITF applications for your company.</p>";
        node["title"] = "Home";
        service.Update(new Entity("powerpagecomponent", page.Id) { ["content"] = node.ToJsonString() });
        Console.WriteLine("portal Home");
    }

    static string PageContent(string title, string url, int order, string? parent, string? root, string? copy)
    {
        var content = new Dictionary<string, object?>
        {
            ["displayorder"] = order,
            ["enablerating"] = false,
            ["enabletracking"] = false,
            ["excludefromsearch"] = false,
            ["feedbackpolicy"] = 756150005,
            ["hiddenfromsitemap"] = false,
            ["isroot"] = root == null,
            ["pagetemplateid"] = PageTemplate,
            ["partialurl"] = url,
            ["publishingstateid"] = PublishingState,
            ["sharedpageconfiguration"] = false,
            ["title"] = title
        };
        if (parent != null) content["parentpageid"] = parent;
        if (root != null) content["rootwebpageid"] = root;
        if (copy != null) content["copy"] = copy;
        return Json(content);
    }

    static string LinkContent(int order, string pageId) => Json(new Dictionary<string, object?>
    {
        ["disablepagevalidation"] = false,
        ["displayimageonly"] = false,
        ["displayorder"] = order,
        ["displaypagechildlinks"] = false,
        ["openinnewwindow"] = false,
        ["pageid"] = pageId,
        ["publishingstateid"] = PublishingState,
        ["robotsfollowlink"] = true,
        ["weblinksetid"] = LinkSet
    });

    static string BasicForm(string entity, string formName, int mode, string success, string? redirect, bool appendRecord, string button = "Save")
    {
        var content = new Dictionary<string, object?>
        {
            ["entityname"] = entity,
            ["formname"] = formName,
            ["mode"] = mode,
            ["tabname"] = "General",
            ["onsuccess"] = redirect == null ? 756150000 : 756150001,
            ["successmessage"] = "[{\"LCID\":1033,\"Value\":\"" + success + "\"}]",
            ["entitypermissionsenabled"] = true,
            ["captcharequired"] = false,
            ["attachfile"] = false,
            ["attachfileallowmultiple"] = false,
            ["attachfilerequired"] = false,
            ["attachfilerestrictaccept"] = false,
            ["showcaptchaforauthenticatedusers"] = false,
            ["hideformonsuccess"] = false,
            ["showownerfields"] = false,
            ["showunsupportedfields"] = false,
            ["tooltipenabled"] = false,
            ["validationsummarylinksenabled"] = true,
            ["autogeneratesteps"] = false,
            ["forceallfieldsrequired"] = false,
            ["recommendedfieldsrequired"] = false,
            ["recordidquerystringparametername"] = "id",
            ["entitysourcetype"] = 756150001,
            ["geolocation_enabled"] = false,
            ["submitbuttontext"] = "[{\"LCID\":1033,\"Value\":\"" + button + "\"}]"
        };
        if (redirect != null)
        {
            content["redirectwebpage"] = redirect;
            content["redirecturlappendentityidquerystring"] = appendRecord;
            content["recordidquerystringparametername"] = "id";
        }
        return Json(content);
    }

    static string ItemBasicForm()
    {
        var content = JsonNode.Parse(BasicForm("cicapos_fundingitem", "Portal", 100000000, "Funding item saved.", null, false))!.AsObject();
        content["setentityreference"] = true;
        content["referenceentitysourcetype"] = 756150000;
        content["referenceentitylogicalname"] = "cicapos_fundingapplication";
        content["referencequerystringname"] = "id";
        content["referencequerystringisprimarykey"] = true;
        content["referenceentityrelationshipname"] = "cicapos_item_application";
        content["referenceentityreadonly"] = true;
        return content.ToJsonString();
    }

    static string List(string entity, string viewId, string empty, string? details) => Json(new Dictionary<string, object?>
    {
        ["entityname"] = entity,
        ["view"] = viewId,
        ["views"] = "{\"" + viewId + "\":[]}",
        ["pagesize"] = 15,
        ["entitypermissionsenabled"] = true,
        ["searchenabled"] = true,
        ["emptylisttext"] = "[{\"LCID\":1033,\"Value\":\"" + empty + "\"}]",
        ["idquerystringparametername"] = "id",
        ["detailsbuttonlabel"] = "[{\"LCID\":1033,\"Value\":\"Open\"}]",
        ["webpagefordetailsview"] = details,
        ["odata_enabled"] = false,
        ["map_enabled"] = false,
        ["calendar_enabled"] = false,
        ["filter_enabled"] = false
    });

    static string MyApplicationsHtml() => """
        <div class="container" style="padding:32px 0;max-width:960px;">
          <h1>My Applications</h1>
          <p>These are the records for your company. Sign in with the contact for that company.</p>
          <p>
            <a class="btn btn-primary" href="/funding-application/">New Funding Application</a>
            <a class="btn btn-default" href="/disbursement-request/">New Disbursement Request</a>
            <a class="btn btn-default" href="/pre-approved-list/">New Pre-approved List Application</a>
          </p>
          <h2>Funding Applications</h2>
          {% entitylist id:a5100001-e2e1-4111-8111-6045bd591020 %}
          <h2>Disbursement Requests</h2>
          {% entitylist id:a5100001-e2e1-4111-8111-6045bd591021 %}
          <h2>Pre-approved List Applications</h2>
          {% entitylist id:a5100001-e2e1-4111-8111-6045bd591022 %}
        </div>
        """;

    static string FundingHtml() => """
        <div class="container" style="padding:32px 0;max-width:960px;">
          <h1>Funding Application</h1>
          {% if request.params.id %}
            <p><a href="/my-applications/">Back to My Applications</a></p>
            {% entityform id:a5100001-e2e1-4111-8111-6045bd591011 %}
            <h2>Funding Item</h2>
            <p>Add each item here. A pre-approved item needs 1 quotation. An NPA item needs 3. Say whether the quotation is the lowest.</p>
            {% entityform id:a5100001-e2e1-4111-8111-6045bd591013 %}
            <h2>Submit</h2>
            <p>Submit when the items and the pledged result date are ready. The route is set from the items.</p>
            {% entityform id:a5100001-e2e1-4111-8111-6045bd591012 %}
          {% else %}
            <p>Save the draft first, then add the funding items.</p>
            {% entityform id:a5100001-e2e1-4111-8111-6045bd591010 %}
          {% endif %}
        </div>
        """;

    static string DisbursementHtml() => """
        <div class="container" style="padding:32px 0;max-width:960px;">
          <h1>Disbursement Request</h1>
          <p><a href="/my-applications/">Back to My Applications</a></p>
          <p>Choose an approved Funding Application. The first instalment cannot exceed 80 percent of the approved amount. MiC and MPD are sent to review.</p>
          {% entityform id:a5100001-e2e1-4111-8111-6045bd591014 %}
        </div>
        """;

    static string PalHtml() => """
        <div class="container" style="padding:32px 0;max-width:960px;">
          <h1>Pre-approved List Application</h1>
          <p><a href="/my-applications/">Back to My Applications</a></p>
          <p>Ask for a product to be listed, or request a change to a listed product.</p>
          {% entityform id:a5100001-e2e1-4111-8111-6045bd591015 %}
        </div>
        """;

    static string FundingApplicationForm() => Form("cicapos_fundingapplication",
        [("Project name", "cicapos_projectname", "4273EDBD-AC1D-40d3-9FB2-095C621B552D"),
         ("Objective", "cicapos_objective", "E0DECE4B-6FC8-4a8f-A065-082708572369"),
         ("Pledged result date", "cicapos_pledgedate", "5B773807-9FB2-42db-97C3-7A91EFF8ADFF"),
         ("Amount applied (HK$)", "cicapos_amountapplied", "C3EFE0C3-0EC6-42be-8349-CBD9079DFD8E"),
         ("Applicant", "cicapos_applicantid", "270BD3DB-D9AF-4782-9025-509E298DEC0A"),
         ("Contact", "cicapos_contactid", "270BD3DB-D9AF-4782-9025-509E298DEC0A")],
        ("Funding Items", "cicapos_item_application", "cicapos_fundingitem", ActiveItemView));

    static string SubmitForm() => Form("cicapos_fundingapplication",
        [("Project name", "cicapos_projectname", "4273EDBD-AC1D-40d3-9FB2-095C621B552D")]);

    static string FundingItemForm() => Form("cicapos_fundingitem",
        [("Funding application", "cicapos_fundingapplicationid", "270BD3DB-D9AF-4782-9025-509E298DEC0A"),
         ("Item type", "cicapos_itemtype", "3EF39988-22BB-4f0b-BBBE-64B5A3748AEE"),
         ("Funding scheme", "cicapos_schemeid", "270BD3DB-D9AF-4782-9025-509E298DEC0A"),
         ("Quantity", "cicapos_quantity", "C3EFE0C3-0EC6-42be-8349-CBD9079DFD8E"),
         ("Unit price (HK$)", "cicapos_unitprice", "C3EFE0C3-0EC6-42be-8349-CBD9079DFD8E"),
         ("Lowest quotation", "cicapos_islowestquote", "67FAC785-CD58-4f9f-ABB3-4B7DDC6ED5ED"),
         ("Justification", "cicapos_justification", "E0DECE4B-6FC8-4a8f-A065-082708572369")]);

    static string DisbursementFormXml() => Form("cicapos_disbursementrequest",
        [("Funding application", "cicapos_fundingapplicationid", "270BD3DB-D9AF-4782-9025-509E298DEC0A"),
         ("Instalment", "cicapos_instalmenttype", "3EF39988-22BB-4f0b-BBBE-64B5A3748AEE"),
         ("Claimed amount (HK$)", "cicapos_claimedamount", "C3EFE0C3-0EC6-42be-8349-CBD9079DFD8E")]);

    static string PalFormXml() => Form("cicapos_palapplication",
        [("Product name", "cicapos_productname", "4273EDBD-AC1D-40d3-9FB2-095C621B552D"),
         ("Request kind", "cicapos_kind", "3EF39988-22BB-4f0b-BBBE-64B5A3748AEE"),
         ("Funding scheme", "cicapos_schemeid", "270BD3DB-D9AF-4782-9025-509E298DEC0A"),
         ("Proposed price (HK$)", "cicapos_proposedprice", "C3EFE0C3-0EC6-42be-8349-CBD9079DFD8E"),
         ("Applicant", "cicapos_applicantid", "270BD3DB-D9AF-4782-9025-509E298DEC0A"),
         ("Note", "cicapos_note", "E0DECE4B-6FC8-4a8f-A065-082708572369")]);

    static string Cell(string inner, int colspan = 2, int? rowspan = null)
    {
        var span = rowspan == null ? "" : $" rowspan=\"{rowspan}\"";
        return $"<row><cell id=\"{{{Guid.NewGuid()}}}\" colspan=\"{colspan}\"{span}>{inner}</cell></row>";
    }

    static string Form(string entity, (string Label, string Field, string ClassId)[] fields, (string Label, string Relationship, string Target, string View)? subgrid = null)
    {
        var rows = string.Concat(fields.Select(field => Cell(
            $"<labels><label description=\"{field.Label}\" languagecode=\"1033\" /></labels>" +
            $"<control id=\"{field.Field}\" classid=\"{{{field.ClassId}}}\" datafieldname=\"{field.Field}\" />")));
        if (subgrid != null)
        {
            var grid = subgrid.Value;
            rows += Cell(
                $"<labels><label description=\"{grid.Label}\" languagecode=\"1033\" /></labels>" +
                "<control id=\"items\" classid=\"{E7A81278-8635-4d9e-8D4D-59480B391C5B}\" indicationOfSubgrid=\"true\">" +
                "<parameters>" +
                $"<ViewId>{{{grid.View}}}</ViewId><IsUserView>false</IsUserView>" +
                $"<RelationshipName>{grid.Relationship}</RelationshipName>" +
                $"<TargetEntityType>{grid.Target}</TargetEntityType>" +
                "<AutoExpand>Fixed</AutoExpand><EnableQuickFind>false</EnableQuickFind>" +
                "<EnableViewPicker>false</EnableViewPicker><EnableJumpBar>false</EnableJumpBar>" +
                "<ChartGridMode>Grid</ChartGridMode><IsUserChart>false</IsUserChart>" +
                "<EnableChartPicker>false</EnableChartPicker><RecordsPerPage>4</RecordsPerPage>" +
                "</parameters></control>",
                rowspan: 6);
        }
        return "<form shownavigationbar=\"false\" showImage=\"false\" maxWidth=\"1900\"><tabs>" +
            $"<tab verticallayout=\"true\" id=\"{{{Guid.NewGuid()}}}\" IsUserDefined=\"1\" name=\"General\">" +
            "<labels><label description=\"General\" languagecode=\"1033\" /></labels>" +
            "<columns><column width=\"100%\"><sections>" +
            $"<section showlabel=\"false\" showbar=\"false\" IsUserDefined=\"0\" id=\"{{{Guid.NewGuid()}}}\" name=\"Details\" columns=\"11\">" +
            "<labels><label description=\"Details\" languagecode=\"1033\" /></labels>" +
            $"<rows>{rows}</rows></section></sections></column></columns></tab></tabs></form>";
    }

    static Guid EnsureApplication(ServiceClient service, Guid account, Guid contact, string project, string objective, DateTime pledge, decimal? grant)
    {
        var id = Find(service, "cicapos_fundingapplication", "cicapos_projectname", project);
        if (id != null) return id.Value;
        var entity = new Entity("cicapos_fundingapplication")
        {
            ["cicapos_projectname"] = project,
            ["cicapos_objective"] = objective,
            ["cicapos_pledgedate"] = pledge,
            ["cicapos_applicantid"] = new EntityReference("account", account),
            ["cicapos_contactid"] = new EntityReference("contact", contact),
            ["cicapos_status"] = new OptionSetValue(542400000)
        };
        if (grant != null) entity["cicapos_amountapplied"] = grant;
        return service.Create(entity);
    }

    static void EnsureItem(ServiceClient service, Guid application, Guid scheme, int itemType, decimal quantity, decimal price, bool lowest, string? justification, string name)
    {
        var query = new QueryExpression("cicapos_fundingitem") { ColumnSet = new ColumnSet("cicapos_fundingitemid"), TopCount = 1 };
        query.Criteria.AddCondition("cicapos_fundingapplicationid", ConditionOperator.Equal, application);
        query.Criteria.AddCondition("cicapos_name", ConditionOperator.Equal, name);
        if (service.RetrieveMultiple(query).Entities.Count > 0) return;
        var entity = new Entity("cicapos_fundingitem")
        {
            ["cicapos_name"] = name,
            ["cicapos_fundingapplicationid"] = new EntityReference("cicapos_fundingapplication", application),
            ["cicapos_schemeid"] = new EntityReference("cicapos_fundingscheme", scheme),
            ["cicapos_itemtype"] = new OptionSetValue(itemType),
            ["cicapos_quantity"] = quantity,
            ["cicapos_unitprice"] = price,
            ["cicapos_islowestquote"] = lowest
        };
        if (justification != null) entity["cicapos_justification"] = justification;
        service.Create(entity);
    }

    static void EnsureSubmitted(ServiceClient service, Guid application)
    {
        var current = service.Retrieve("cicapos_fundingapplication", application, new ColumnSet("cicapos_status"));
        var status = current.GetAttributeValue<OptionSetValue>("cicapos_status")?.Value ?? 542400000;
        if (status == 542400000)
            service.Update(new Entity("cicapos_fundingapplication", application) { ["cicapos_status"] = new OptionSetValue(542400001) });
    }

    static void SubmitThenApprove(ServiceClient service, Guid application, decimal grant)
    {
        EnsureSubmitted(service, application);
        var current = service.Retrieve("cicapos_fundingapplication", application, new ColumnSet("cicapos_status"));
        if (current.GetAttributeValue<OptionSetValue>("cicapos_status")?.Value != 542400009)
        {
            service.Update(new Entity("cicapos_fundingapplication", application)
            {
                ["cicapos_status"] = new OptionSetValue(542400009),
                ["cicapos_approvedgrant"] = grant
            });
        }
    }

    static void EnsureDisbursement(ServiceClient service, Guid application, decimal claimed, string label)
    {
        var query = new QueryExpression("cicapos_disbursementrequest") { ColumnSet = new ColumnSet("cicapos_disbursementrequestid"), TopCount = 1 };
        query.Criteria.AddCondition("cicapos_fundingapplicationid", ConditionOperator.Equal, application);
        query.Criteria.AddCondition("cicapos_claimedamount", ConditionOperator.Equal, claimed);
        if (service.RetrieveMultiple(query).Entities.Count > 0) return;
        service.Create(new Entity("cicapos_disbursementrequest")
        {
            ["cicapos_fundingapplicationid"] = new EntityReference("cicapos_fundingapplication", application),
            ["cicapos_instalmenttype"] = new OptionSetValue(542400000),
            ["cicapos_claimedamount"] = claimed,
            ["cicapos_status"] = new OptionSetValue(542400001)
        });
        Console.WriteLine("disbursement " + label);
    }

    static void EnsurePal(ServiceClient service, Guid account, Guid scheme, string product, int kind, decimal price, string note, bool submit)
    {
        var id = Find(service, "cicapos_palapplication", "cicapos_productname", product);
        if (id == null)
        {
            id = service.Create(new Entity("cicapos_palapplication")
            {
                ["cicapos_productname"] = product,
                ["cicapos_kind"] = new OptionSetValue(kind),
                ["cicapos_schemeid"] = new EntityReference("cicapos_fundingscheme", scheme),
                ["cicapos_proposedprice"] = price,
                ["cicapos_note"] = note,
                ["cicapos_applicantid"] = new EntityReference("account", account),
                ["cicapos_status"] = new OptionSetValue(542400000)
            });
        }
        if (!submit) return;
        var current = service.Retrieve("cicapos_palapplication", id.Value, new ColumnSet("cicapos_status"));
        if (current.GetAttributeValue<OptionSetValue>("cicapos_status")?.Value == 542400000)
            service.Update(new Entity("cicapos_palapplication", id.Value) { ["cicapos_status"] = new OptionSetValue(542400001) });
    }

    static Dictionary<string, Guid> Schemes(ServiceClient service)
    {
        var query = new QueryExpression("cicapos_fundingscheme") { ColumnSet = new ColumnSet("cicapos_code") };
        query.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0);
        var map = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in service.RetrieveMultiple(query).Entities)
        {
            var code = (row.GetAttributeValue<string>("cicapos_code") ?? "").Trim().ToUpperInvariant();
            if (code.Length > 0) map[code] = row.Id;
        }
        return map;
    }

    static Guid FindOrCreate(ServiceClient service, string entity, string attribute, string value, Func<Entity> create)
    {
        var found = Find(service, entity, attribute, value);
        return found ?? service.Create(create());
    }

    static Guid? Find(ServiceClient service, string entity, string attribute, string value)
    {
        var query = new QueryExpression(entity) { ColumnSet = new ColumnSet(false), TopCount = 1 };
        query.Criteria.AddCondition(attribute, ConditionOperator.Equal, value);
        return service.RetrieveMultiple(query).Entities.FirstOrDefault()?.Id;
    }

    static Entity First(this QueryExpression query, ServiceClient service) => service.RetrieveMultiple(query).Entities.First();

    static string Json(Dictionary<string, object?> content) => JsonSerializer.Serialize(content);
}
