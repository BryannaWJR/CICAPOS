#if NETFRAMEWORK
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace CICAPOS.Plugins;

public sealed class DisbursementRoutePlugin : IPlugin
{
    public void Execute(IServiceProvider serviceProvider)
    {
        var service = PluginKit.Service(serviceProvider, out var context);
        if (context.Depth > 1 || context.MessageName is not ("Create" or "Update")) return;
        var target = PluginKit.Target(context);
        if (target?.LogicalName != "cicapos_disbursementrequest") return;
        if (context.Stage == 40)
        {
            ApplySpotCheck(service, target.Id != Guid.Empty ? target.Id : context.PrimaryEntityId);
            return;
        }
        if (context.Stage != 20) return;
        Apply(service, target);
    }

    static void Apply(IOrganizationService service, Entity target)
    {
        var (merged, _) = PluginKit.Merge(service, target,
            "cicapos_status", "cicapos_fundingapplicationid", "cicapos_instalmenttype", "cicapos_claimedamount", "cicapos_name", "cicapos_submittedon", "cicapos_reviewrequired", "cicapos_spotcheckselected");
        var status = PluginKit.Option(merged, "cicapos_status", Rules.Draft);
        if (status == Rules.Draft) return;

        var parent = merged.GetAttributeValue<EntityReference>("cicapos_fundingapplicationid")
            ?? throw new InvalidPluginExecutionException("Choose the funding application before submitting a disbursement.");
        var application = service.Retrieve(parent.LogicalName, parent.Id, new ColumnSet("cicapos_status", "cicapos_approvedgrant"));
        if (PluginKit.Option(application, "cicapos_status", Rules.Draft) != Rules.FundingApproved)
            throw new InvalidPluginExecutionException("Only an approved funding application can be disbursed.");

        var items = PluginKit.Items(service, parent.Id);
        var limits = Limits.Load(service);
        var review = Rules.NeedsReview(items.Select(item => item.Fact.SchemeCode));
        target["cicapos_reviewrequired"] = review;
        var reference = merged.GetAttributeValue<string>("cicapos_name");
        if (review || !string.IsNullOrWhiteSpace(reference))
            target["cicapos_spotcheckselected"] = !review && Rules.SelectedForSpotCheck(reference, limits.SpotEvery);
        if (status == Rules.Submitted && merged.GetAttributeValue<DateTime?>("cicapos_submittedon") == null)
            target["cicapos_submittedon"] = DateTime.UtcNow;

        var claimed = merged.GetAttributeValue<decimal>("cicapos_claimedamount");
        var instalment = PluginKit.Option(merged, "cicapos_instalmenttype", Rules.FirstInstalment);
        if (instalment == Rules.FirstInstalment)
        {
            var budget = application.GetAttributeValue<decimal>("cicapos_approvedgrant");
            if (budget <= 0)
                budget = items.Sum(item => item.ApprovedAmount);
            if (!Rules.WithinFirstInstalment(claimed, budget, limits.FirstPercent))
            {
                var limit = decimal.Round(budget * limits.FirstPercent / 100m, 2, MidpointRounding.AwayFromZero);
                throw new InvalidPluginExecutionException($"The first instalment cannot exceed {limits.FirstPercent:0}% of the approved amount (HK${limit:N0}).");
            }
        }
        if (instalment == Rules.FinalInstalment && !FinalAllowed(service, parent.Id, target.Id, limits.FinalMonths))
            throw new InvalidPluginExecutionException(FirstPayment(service, parent.Id, target.Id) == null
                ? "A final payment needs an approved first instalment."
                : $"The final payment can be requested {limits.FinalMonths} months after the first payment.");
    }

    static void ApplySpotCheck(IOrganizationService service, Guid id)
    {
        if (id == Guid.Empty) return;
        var current = service.Retrieve("cicapos_disbursementrequest", id, new ColumnSet("cicapos_status", "cicapos_name", "cicapos_fundingapplicationid", "cicapos_spotcheckselected"));
        if (PluginKit.Option(current, "cicapos_status", Rules.Draft) == Rules.Draft) return;
        var parent = current.GetAttributeValue<EntityReference>("cicapos_fundingapplicationid");
        if (parent == null) return;
        var review = Rules.NeedsReview(PluginKit.Items(service, parent.Id).Select(item => item.Fact.SchemeCode));
        var selected = !review && Rules.SelectedForSpotCheck(current.GetAttributeValue<string>("cicapos_name"), Limits.Load(service).SpotEvery);
        if (PluginKit.Bit(current, "cicapos_spotcheckselected") == selected) return;
        service.Update(new Entity(current.LogicalName, current.Id) { ["cicapos_spotcheckselected"] = selected });
    }

    static bool FinalAllowed(IOrganizationService service, Guid applicationId, Guid currentId, int months)
    {
        var first = FirstPayment(service, applicationId, currentId);
        var on = first?.GetAttributeValue<DateTime?>("cicapos_submittedon") ?? first?.GetAttributeValue<DateTime?>("createdon");
        return Rules.FinalInstalmentAllowed(on, months, DateTime.UtcNow);
    }

    static Entity? FirstPayment(IOrganizationService service, Guid applicationId, Guid currentId)
    {
        var query = new QueryExpression("cicapos_disbursementrequest")
        {
            ColumnSet = new ColumnSet("cicapos_submittedon", "createdon"),
            TopCount = 1
        };
        query.Criteria.AddCondition("cicapos_fundingapplicationid", ConditionOperator.Equal, applicationId);
        query.Criteria.AddCondition("cicapos_instalmenttype", ConditionOperator.Equal, Rules.FirstInstalment);
        query.Criteria.AddCondition("cicapos_status", ConditionOperator.Equal, Rules.DisbursementApproved);
        query.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0);
        if (currentId != Guid.Empty)
            query.Criteria.AddCondition("cicapos_disbursementrequestid", ConditionOperator.NotEqual, currentId);
        query.AddOrder("createdon", OrderType.Ascending);
        return service.RetrieveMultiple(query).Entities.FirstOrDefault();
    }

}
#endif
