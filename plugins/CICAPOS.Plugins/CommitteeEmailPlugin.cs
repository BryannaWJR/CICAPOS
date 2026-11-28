#if NETFRAMEWORK
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace CICAPOS.Plugins;

/// <summary>
/// Portal Web API cannot create activityparty rows. Approval-request emails are completed here:
/// the committee address is attached while the email is created, then the email is sent.
/// </summary>
public sealed class CommitteeEmailPlugin : IPlugin
{
    public const string Address = "brwei@deloittecn.com.cn";
    public const string Prefix = "CITF approval request";

    public void Execute(IServiceProvider serviceProvider)
    {
        var service = PluginKit.Service(serviceProvider, out var context);
        if (context.Depth > 1 || context.MessageName != "Create" || context.PrimaryEntityName != "email") return;
        var target = PluginKit.Target(context);
        if (target == null) return;
        var subject = target.GetAttributeValue<string>("subject") ?? "";
        if (!IsApproval(subject)) return;

        if (context.Stage == 20)
        {
            target["to"] = new EntityCollection(new[] { Party(2, Address, null) });
            var sender = Sender(service, context.UserId);
            if (sender != null)
                target["from"] = new EntityCollection(new[] { Party(1, null, sender) });
            return;
        }

        if (context.Stage != 40) return;
        var id = context.PrimaryEntityId != Guid.Empty ? context.PrimaryEntityId : target.Id;
        if (id == Guid.Empty) return;
        service.Execute(new SendEmailRequest { EmailId = id, IssueSend = true });
    }

    static bool IsApproval(string subject)
        => subject.StartsWith(Prefix, StringComparison.Ordinal)
           || (subject.StartsWith("Case ", StringComparison.Ordinal) && subject.IndexOf("Approval Request", StringComparison.Ordinal) >= 0);

    static Entity Party(int mask, string? address, EntityReference? who)
    {
        var row = new Entity("activityparty");
        row["participationtypemask"] = new OptionSetValue(mask);
        if (!string.IsNullOrWhiteSpace(address)) row["addressused"] = address;
        if (who != null) row["partyid"] = who;
        return row;
    }

    static EntityReference? Sender(IOrganizationService service, Guid fallbackUserId)
    {
        try
        {
            var query = new QueryExpression("mailbox")
            {
                ColumnSet = new ColumnSet("regardingobjectid"),
                TopCount = 1
            };
            query.Criteria.AddCondition("outgoingemailstatus", ConditionOperator.Equal, 1);
            query.Criteria.AddCondition("regardingobjectid", ConditionOperator.NotNull);
            var regarding = service.RetrieveMultiple(query).Entities.FirstOrDefault()?.GetAttributeValue<EntityReference>("regardingobjectid");
            if (regarding != null && regarding.LogicalName is "systemuser" or "queue") return regarding;
        }
        catch (Exception)
        {
            // A missing mailbox privilege should not hide the approval email. Fall back to the caller.
        }
        return fallbackUserId == Guid.Empty ? null : new EntityReference("systemuser", fallbackUserId);
    }
}
#endif
