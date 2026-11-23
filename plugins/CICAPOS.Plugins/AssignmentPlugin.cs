#if NETFRAMEWORK
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace CICAPOS.Plugins;

public sealed class AssignmentPlugin : IPlugin
{
    public void Execute(IServiceProvider serviceProvider)
    {
        var service = PluginKit.Service(serviceProvider, out var context);
        if (context.Depth > 1 || context.MessageName is not ("Create" or "Update")) return;
        var target = PluginKit.Target(context);
        if (target == null || !Profiles.TryGetValue(target.LogicalName, out var profile)) return;

        var columns = new List<string> { "cicapos_status", "cicapos_casehandlerid", "cicapos_submittedon" };
        if (target.LogicalName == "cicapos_palapplication") columns.Add("cicapos_schemeid");
        if (target.LogicalName == "cicapos_disbursementrequest") columns.Add("cicapos_fundingapplicationid");
        var (merged, previous) = PluginKit.Merge(service, target, columns.ToArray());
        var next = PluginKit.Option(merged, "cicapos_status", previous);
        if (next != Rules.Submitted || previous == Rules.Submitted) return;

        var handler = merged.GetAttributeValue<EntityReference>("cicapos_casehandlerid");
        if (handler == null)
        {
            var chosen = Choose(service, target);
            if (chosen == null) return;
            handler = new EntityReference("systemuser", chosen.Value);
        }

        target["cicapos_casehandlerid"] = handler;
        target["ownerid"] = new EntityReference("systemuser", handler.Id);
        PluginKit.SetOption(target, "cicapos_status", Rules.Assigned);
        if (merged.GetAttributeValue<DateTime?>("cicapos_submittedon") == null)
            target["cicapos_submittedon"] = DateTime.UtcNow;
    }

    static Guid? Choose(IOrganizationService service, Entity target)
    {
        var profile = Profiles[target.LogicalName];
        var schemes = Schemes(service, target);
        var rules = PluginKit.All(service, RulesQuery(profile.Process));
        var officers = rules.Select(rule => rule.GetAttributeValue<EntityReference>("cicapos_officerid")?.Id ?? Guid.Empty).Where(id => id != Guid.Empty).Distinct().ToList();
        var open = OpenCases(service, target, profile, officers);
        var candidates = rules.Select(rule => new OfficerCandidate(
            rule.GetAttributeValue<EntityReference>("cicapos_officerid")?.Id ?? Guid.Empty,
            rule.GetAttributeValue<EntityReference>("cicapos_schemeid")?.Id is Guid scheme && scheme != Guid.Empty ? scheme : null,
            rule.GetAttributeValue<string>("cicapos_expertise"),
            open.TryGetValue(rule.GetAttributeValue<EntityReference>("cicapos_officerid")?.Id ?? Guid.Empty, out var count) ? count : 0));
        return Rules.ChooseOfficer(candidates, schemes.Ids, schemes.Codes);
    }

    static QueryExpression RulesQuery(int process)
    {
        var query = new QueryExpression("cicapos_assignmentrule")
        {
            ColumnSet = new ColumnSet("cicapos_officerid", "cicapos_schemeid", "cicapos_expertise")
        };
        query.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0);
        query.Criteria.AddCondition("cicapos_isavailable", ConditionOperator.Equal, true);
        query.Criteria.AddCondition("cicapos_process", ConditionOperator.Equal, process);
        return query;
    }

    static Dictionary<Guid, int> OpenCases(IOrganizationService service, Entity target, ProcessProfile profile, List<Guid> officers)
    {
        var counts = officers.ToDictionary(id => id, _ => 0);
        if (officers.Count == 0) return counts;
        var query = new QueryExpression(target.LogicalName) { ColumnSet = new ColumnSet("cicapos_casehandlerid") };
        query.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0);
        query.Criteria.AddCondition("cicapos_casehandlerid", ConditionOperator.In, officers.Cast<object>().ToArray());
        query.Criteria.AddCondition("cicapos_status", ConditionOperator.NotIn, profile.Closed.Cast<object>().ToArray());
        if (target.Id != Guid.Empty)
            query.Criteria.AddCondition(target.LogicalName + "id", ConditionOperator.NotEqual, target.Id);
        foreach (var row in PluginKit.All(service, query))
        {
            var officer = row.GetAttributeValue<EntityReference>("cicapos_casehandlerid")?.Id ?? Guid.Empty;
            if (counts.ContainsKey(officer)) counts[officer]++;
        }
        return counts;
    }

    static (IReadOnlyCollection<Guid> Ids, IReadOnlyCollection<string> Codes) Schemes(IOrganizationService service, Entity target)
    {
        if (target.LogicalName == "cicapos_palapplication")
        {
            var (merged, _) = PluginKit.Merge(service, target, "cicapos_schemeid");
            var scheme = merged.GetAttributeValue<EntityReference>("cicapos_schemeid");
            return scheme == null ? ([], []) : ([scheme.Id], Codes(service, [scheme.Id]));
        }
        var applicationId = target.LogicalName == "cicapos_fundingapplication"
            ? target.Id
            : PluginKit.Merge(service, target, "cicapos_fundingapplicationid").Merged.GetAttributeValue<EntityReference>("cicapos_fundingapplicationid")?.Id ?? Guid.Empty;
        if (applicationId == Guid.Empty) return ([], []);
        var items = PluginKit.Items(service, applicationId);
        var ids = items.Where(item => item.SchemeId != null).Select(item => item.SchemeId!.Value).Distinct().ToList();
        return (ids, items.Select(item => Rules.Normalize(item.Fact.SchemeCode)).Where(code => code.Length > 0).Distinct().ToList());
    }

    static List<string> Codes(IOrganizationService service, IReadOnlyCollection<Guid> ids)
    {
        if (ids.Count == 0) return [];
        var query = new QueryExpression("cicapos_fundingscheme") { ColumnSet = new ColumnSet("cicapos_code") };
        query.Criteria.AddCondition("cicapos_fundingschemeid", ConditionOperator.In, ids.Cast<object>().ToArray());
        return PluginKit.All(service, query).Select(row => Rules.Normalize(row.GetAttributeValue<string>("cicapos_code"))).Where(code => code.Length > 0).Distinct().ToList();
    }

    static readonly Dictionary<string, ProcessProfile> Profiles = new(StringComparer.OrdinalIgnoreCase)
    {
        ["cicapos_fundingapplication"] = new(542400000, [Rules.FundingApproved, 542400010, 542400011]),
        ["cicapos_disbursementrequest"] = new(542400001, [Rules.DisbursementApproved, 542400007]),
        ["cicapos_palapplication"] = new(542400002, [542400008, 542400009, 542400010])
    };
}

sealed record ProcessProfile(int Process, int[] Closed);
#endif
