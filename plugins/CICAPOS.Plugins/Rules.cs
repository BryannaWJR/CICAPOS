namespace CICAPOS.Plugins;

public sealed record FundingItemFact(int ItemType, string? SchemeCode, decimal Quantity, decimal UnitPrice, bool? IsLowestQuote, string? Justification);

public sealed record OfficerCandidate(Guid OfficerId, Guid? SchemeId, string? Expertise, int OpenCases);

public static class Rules
{
    public const int PreapprovedItem = 542400000;
    public const int PreviouslyApprovedNpa = 542400001;
    public const int NewNpa = 542400002;
    public const int ShortRoute = 542400000;
    public const int LongRoute = 542400001;

    public const int Draft = 542400000;
    public const int Submitted = 542400001;
    public const int PendingEndorsement = 542400012;
    public const int Assigned = 542400002;
    public const int FundingApproved = 542400009;
    public const int DisbursementApproved = 542400006;
    public const int FirstInstalment = 542400000;
    public const int FinalInstalment = 542400002;

    public static readonly string[] ShortSchemeCodes = ["ACT", "BIM", "STB"];
    public static readonly string[] ReviewSchemeCodes = ["MIC", "MPD"];

    public static int Route(IEnumerable<FundingItemFact> items)
        => items.Any(item => item.ItemType == NewNpa) ? LongRoute : ShortRoute;

    public static int QuotationsRequired(int itemType, int preapprovedQuotes, int npaQuotes)
        => itemType is PreviouslyApprovedNpa or NewNpa ? npaQuotes : preapprovedQuotes;

    public static decimal CombinedAmount(IEnumerable<FundingItemFact> items, IReadOnlyCollection<string> schemeCodes)
        => items.Where(item => schemeCodes.Contains(Normalize(item.SchemeCode))).Sum(item => item.Quantity * item.UnitPrice);

    public static IReadOnlyList<string> ItemProblems(FundingItemFact item, int requiredQuotes)
    {
        var problems = new List<string>();
        if (item.IsLowestQuote == null)
            problems.Add("Say whether the quotation is the lowest.");
        else if (item.IsLowestQuote == false && string.IsNullOrWhiteSpace(item.Justification))
            problems.Add("A quotation that is not the lowest needs a justification.");
        if (item.Quantity < 0 || item.UnitPrice < 0)
            problems.Add("Quantity and unit price cannot be negative.");
        _ = requiredQuotes;
        return problems;
    }

    public static bool NeedsReview(IEnumerable<string?> schemeCodes)
        => schemeCodes.Any(code => ReviewSchemeCodes.Contains(Normalize(code)));

    public static bool WithinFirstInstalment(decimal claimed, decimal approvedGrant, decimal percent)
        => approvedGrant > 0 && claimed >= 0 && claimed <= decimal.Round(approvedGrant * percent / 100m, 2, MidpointRounding.AwayFromZero);

    public static bool FinalInstalmentAllowed(DateTime? firstInstalmentOn, int waitMonths, DateTime today)
        => firstInstalmentOn != null && firstInstalmentOn.Value.Date.AddMonths(waitMonths) <= today.Date;

    public static bool SelectedForSpotCheck(string? reference, int every)
    {
        if (string.IsNullOrWhiteSpace(reference) || every <= 0) return false;
        var digits = new string(reference.Where(char.IsDigit).ToArray());
        return digits.Length > 0 && int.TryParse(digits, out var number) && number > 0 && number % every == 0;
    }

    public static Guid? ChooseOfficer(IEnumerable<OfficerCandidate> candidates, IReadOnlyCollection<Guid> schemeIds, IReadOnlyCollection<string> schemeCodes)
    {
        var available = candidates.Where(candidate => candidate.OfficerId != Guid.Empty && ExpertiseMatches(candidate.Expertise, schemeCodes)).ToList();
        var specific = available.Where(candidate => candidate.SchemeId != null && schemeIds.Contains(candidate.SchemeId.Value)).ToList();
        var pool = specific.Count > 0 ? specific : available.Where(candidate => candidate.SchemeId == null).ToList();
        return pool.OrderBy(candidate => candidate.OpenCases).ThenBy(candidate => candidate.OfficerId).Select(candidate => (Guid?)candidate.OfficerId).FirstOrDefault();
    }

    public static string Normalize(string? code) => (code ?? "").Trim().ToUpperInvariant();

    static bool ExpertiseMatches(string? expertise, IReadOnlyCollection<string> schemeCodes)
    {
        if (string.IsNullOrWhiteSpace(expertise)) return true;
        var expected = Normalize(expertise);
        return schemeCodes.Count == 0 || schemeCodes.Contains(expected);
    }
}
