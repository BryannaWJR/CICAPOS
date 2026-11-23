#if NETFRAMEWORK
using System.ServiceModel;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace CICAPOS.Plugins;

sealed class Limits
{
    public decimal ActBimCap { get; init; } = 7_500_000m;
    public int QuotesPreapproved { get; init; } = 1;
    public int QuotesNpa { get; init; } = 3;
    public decimal FirstPercent { get; init; } = 80m;
    public int FinalMonths { get; init; } = 12;
    public int SpotEvery { get; init; } = 10;

    public static Limits Load(IOrganizationService service)
    {
        var query = new QueryExpression("cicapos_fundingrule")
        {
            ColumnSet = new ColumnSet("cicapos_key", "cicapos_value")
        };
        query.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0);
        query.Criteria.AddCondition("cicapos_key", ConditionOperator.In,
            "actBimCap", "quotesPreapproved", "quotesNpa", "firstInstalmentPercent", "finalWaitMonths", "spotCheckEvery");
        var values = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in PluginKit.All(service, query))
        {
            var key = row.GetAttributeValue<string>("cicapos_key");
            if (!string.IsNullOrWhiteSpace(key) && row.Contains("cicapos_value") && row["cicapos_value"] != null)
                values[key] = row.GetAttributeValue<decimal>("cicapos_value");
        }
        return new Limits
        {
            ActBimCap = Value(values, "actBimCap", 7_500_000m),
            QuotesPreapproved = Whole(Value(values, "quotesPreapproved", 1m)),
            QuotesNpa = Whole(Value(values, "quotesNpa", 3m)),
            FirstPercent = Value(values, "firstInstalmentPercent", 80m),
            FinalMonths = Whole(Value(values, "finalWaitMonths", 12m)),
            SpotEvery = Whole(Value(values, "spotCheckEvery", 10m))
        };
    }

    static decimal Value(Dictionary<string, decimal> values, string key, decimal fallback)
        => values.TryGetValue(key, out var value) ? value : fallback;

    static int Whole(decimal value) => (int)decimal.Round(value, 0, MidpointRounding.AwayFromZero);
}

sealed class ItemRow
{
    public Guid Id { get; init; }
    public required FundingItemFact Fact { get; init; }
    public Guid? SchemeId { get; init; }
    public decimal ApprovedAmount { get; init; }
}

static class PluginKit
{
    public static IOrganizationService Service(IServiceProvider provider, out IPluginExecutionContext context)
    {
        context = (IPluginExecutionContext)provider.GetService(typeof(IPluginExecutionContext));
        var factory = (IOrganizationServiceFactory)provider.GetService(typeof(IOrganizationServiceFactory));
        return factory.CreateOrganizationService(context.UserId);
    }

    public static Entity? Target(IPluginExecutionContext context)
        => context.InputParameters.Contains("Target") && context.InputParameters["Target"] is Entity entity ? entity : null;

    public static int Option(Entity entity, string attribute, int fallback)
        => entity.Contains(attribute) && entity[attribute] is OptionSetValue option ? option.Value : fallback;

    public static void SetOption(Entity target, string attribute, int value)
        => target[attribute] = new OptionSetValue(value);

    public static bool? Bit(Entity entity, string attribute)
        => entity.Contains(attribute) && entity[attribute] is bool value ? value : null;

    public static List<Entity> All(IOrganizationService service, QueryExpression query)
    {
        var rows = new List<Entity>();
        query.PageInfo = new PagingInfo { Count = 5000, PageNumber = 1 };
        while (true)
        {
            var page = service.RetrieveMultiple(query);
            rows.AddRange(page.Entities);
            if (!page.MoreRecords || rows.Count >= 20000) return rows;
            query.PageInfo.PageNumber++;
            query.PageInfo.PagingCookie = page.PagingCookie;
        }
    }

    public static (Entity Merged, int Previous) Merge(IOrganizationService service, Entity target, params string[] columns)
    {
        if (target.Id == Guid.Empty)
            return (target, Rules.Draft);
        Entity stored;
        try
        {
            stored = service.Retrieve(target.LogicalName, target.Id, new ColumnSet(columns));
        }
        catch (FaultException<OrganizationServiceFault> fault) when (IsMissing(fault))
        {
            return (target, Rules.Draft);
        }
        var previous = Option(stored, "cicapos_status", Rules.Draft);
        foreach (var attribute in target.Attributes)
            stored[attribute.Key] = attribute.Value;
        return (stored, previous);
    }

    public static List<ItemRow> Items(IOrganizationService service, Guid applicationId)
    {
        var query = new QueryExpression("cicapos_fundingitem")
        {
            ColumnSet = new ColumnSet("cicapos_itemtype", "cicapos_quantity", "cicapos_unitprice", "cicapos_islowestquote", "cicapos_justification", "cicapos_schemeid", "cicapos_approvedamount")
        };
        query.Criteria.AddCondition("cicapos_fundingapplicationid", ConditionOperator.Equal, applicationId);
        query.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0);
        var scheme = query.AddLink("cicapos_fundingscheme", "cicapos_schemeid", "cicapos_fundingschemeid", JoinOperator.LeftOuter);
        scheme.Columns = new ColumnSet("cicapos_code");
        scheme.EntityAlias = "scheme";
        return All(service, query).Select(ToRow).ToList();
    }

    public static ItemRow ToRow(Entity entity)
    {
        var scheme = entity.GetAttributeValue<AliasedValue>("scheme.cicapos_code")?.Value as string;
        var schemeId = entity.GetAttributeValue<EntityReference>("cicapos_schemeid")?.Id;
        return new ItemRow
        {
            Id = entity.Id,
            SchemeId = schemeId == Guid.Empty ? null : schemeId,
            ApprovedAmount = entity.GetAttributeValue<decimal>("cicapos_approvedamount"),
            Fact = new FundingItemFact(
                Option(entity, "cicapos_itemtype", Rules.PreapprovedItem),
                scheme,
                entity.GetAttributeValue<decimal>("cicapos_quantity"),
                entity.GetAttributeValue<decimal>("cicapos_unitprice"),
                Bit(entity, "cicapos_islowestquote"),
                entity.GetAttributeValue<string>("cicapos_justification"))
        };
    }

    public static ItemRow WithScheme(IOrganizationService service, Entity merged, IReadOnlyList<ItemRow> existing)
    {
        var row = ToRow(merged);
        var previous = row.Id == Guid.Empty ? null : existing.FirstOrDefault(item => item.Id == row.Id);
        var code = row.Fact.SchemeCode;
        if (string.IsNullOrWhiteSpace(code))
            code = previous?.Fact.SchemeCode;
        var schemeId = row.SchemeId ?? previous?.SchemeId;
        if (string.IsNullOrWhiteSpace(code) && schemeId is Guid id)
            code = service.Retrieve("cicapos_fundingscheme", id, new ColumnSet("cicapos_code")).GetAttributeValue<string>("cicapos_code");
        return new ItemRow
        {
            Id = row.Id,
            SchemeId = schemeId,
            ApprovedAmount = previous?.ApprovedAmount ?? row.ApprovedAmount,
            Fact = new FundingItemFact(row.Fact.ItemType, code, row.Fact.Quantity, row.Fact.UnitPrice, row.Fact.IsLowestQuote, row.Fact.Justification)
        };
    }

    public static void Replace(List<ItemRow> rows, ItemRow current)
    {
        var index = current.Id == Guid.Empty ? -1 : rows.FindIndex(row => row.Id == current.Id);
        if (index >= 0) rows[index] = current;
        else rows.Add(current);
    }

    public static void Require(IReadOnlyCollection<string> problems)
    {
        if (problems.Count > 0)
            throw new InvalidPluginExecutionException(string.Join(" ", problems));
    }

    static bool IsMissing(FaultException<OrganizationServiceFault> fault)
        => fault.Detail?.ErrorCode == unchecked((int)0x80040217)
            || (fault.Detail?.Message ?? fault.Message).Contains("Does Not Exist");
}
#endif
