#if NETFRAMEWORK
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace CICAPOS.Plugins;

/// <summary>
/// Any new NPA item takes the long route. MiC and MPD stay on the short application route; their extra review happens on disbursement.
/// </summary>
public sealed class FundingRoutePlugin : IPlugin
{
    public void Execute(IServiceProvider serviceProvider)
    {
        var service = PluginKit.Service(serviceProvider, out var context);
        if (context.Depth > 1 || context.MessageName is not ("Create" or "Update")) return;
        var target = PluginKit.Target(context);
        if (target == null) return;
        if (target.LogicalName == "cicapos_fundingitem")
        {
            LockItem(service, target);
            return;
        }
        if (target.LogicalName == "cicapos_fundingapplication")
            Apply(service, target);
    }

    static void LockItem(IOrganizationService service, Entity target)
    {
        var (merged, _) = PluginKit.Merge(service, target, "cicapos_itemtype", "cicapos_quantity", "cicapos_unitprice", "cicapos_islowestquote", "cicapos_justification", "cicapos_schemeid", "cicapos_fundingapplicationid");
        var limits = Limits.Load(service);
        var quotes = Rules.QuotationsRequired(PluginKit.Option(merged, "cicapos_itemtype", Rules.PreapprovedItem), limits.QuotesPreapproved, limits.QuotesNpa);
        if (quotes is < 0 or > 20)
            throw new InvalidPluginExecutionException("The quotation rule must be between 0 and 20.");
        target["cicapos_quotesrequired"] = quotes;

        var parent = merged.GetAttributeValue<EntityReference>("cicapos_fundingapplicationid");
        if (parent == null) return;
        var application = service.Retrieve(parent.LogicalName, parent.Id, new ColumnSet("cicapos_status", "cicapos_routetype"));
        var applicationStatus = PluginKit.Option(application, "cicapos_status", Rules.Draft);
        if (applicationStatus is Rules.Draft or Rules.PendingEndorsement) return;

        var items = PluginKit.Items(service, parent.Id);
        var row = PluginKit.WithScheme(service, merged, items);
        PluginKit.Require(Rules.ItemProblems(row.Fact, quotes).ToList());
        PluginKit.Replace(items, row);
        EnforceCap(items, limits);
        var route = Rules.Route(items.Select(item => item.Fact));
        if (PluginKit.Option(application, "cicapos_routetype", -1) == route) return;
        var update = new Entity(parent.LogicalName, parent.Id);
        PluginKit.SetOption(update, "cicapos_routetype", route);
        service.Update(update);
    }

    static void Apply(IOrganizationService service, Entity target)
    {
        var (merged, _) = PluginKit.Merge(service, target, "cicapos_status", "cicapos_pledgedate", "cicapos_submittedon", "cicapos_routetype");
        var status = PluginKit.Option(merged, "cicapos_status", Rules.Draft);
        if (status == Rules.Draft) return;
        var pendingEndorsement = status == Rules.PendingEndorsement;

        var limits = Limits.Load(service);
        var items = target.Id == Guid.Empty ? new List<ItemRow>() : PluginKit.Items(service, target.Id);
        var problems = new List<string>();
        var pledged = merged.GetAttributeValue<DateTime?>("cicapos_pledgedate");
        if (pledged == null || pledged.Value.Date < DateTime.UtcNow.Date)
            problems.Add("Enter a pledged result date on or after today.");
        if (items.Count == 0)
            problems.Add("Add at least one funding item before submitting.");
        foreach (var item in items)
            problems.AddRange(Rules.ItemProblems(item.Fact, Rules.QuotationsRequired(item.Fact.ItemType, limits.QuotesPreapproved, limits.QuotesNpa)));
        var total = Rules.CombinedAmount(items.Select(item => item.Fact), ["ACT", "BIM"]);
        if (total > limits.ActBimCap)
            problems.Add($"The combined ACT and BIM amount of HK${total:N0} exceeds the cap of HK${limits.ActBimCap:N0}.");
        PluginKit.Require(problems);

        PluginKit.SetOption(target, "cicapos_routetype", Rules.Route(items.Select(item => item.Fact)));
        if (pendingEndorsement) return;
        if (status == Rules.Submitted && merged.GetAttributeValue<DateTime?>("cicapos_submittedon") == null)
            target["cicapos_submittedon"] = DateTime.UtcNow;
    }

    static void EnforceCap(IReadOnlyCollection<ItemRow> items, Limits limits)
    {
        var total = Rules.CombinedAmount(items.Select(item => item.Fact), ["ACT", "BIM"]);
        if (total > limits.ActBimCap)
            throw new InvalidPluginExecutionException($"The combined ACT and BIM amount of HK${total:N0} exceeds the cap of HK${limits.ActBimCap:N0}.");
    }
}
#endif
