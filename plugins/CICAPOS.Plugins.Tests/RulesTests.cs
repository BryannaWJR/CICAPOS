using CICAPOS.Plugins;
using Xunit;

public class RulesTests
{
    static FundingItemFact Item(int type, string? scheme, decimal quantity = 1, decimal price = 100, bool? lowest = true, string? justification = null)
        => new(type, scheme, quantity, price, lowest, justification);

    [Fact]
    public void NewNpaUsesTheLongRoute()
    {
        var route = Rules.Route([Item(Rules.PreapprovedItem, "BIM"), Item(Rules.NewNpa, "ACT")]);
        Assert.Equal(Rules.LongRoute, route);
    }

    [Fact]
    public void PreapprovedAndPreviousNpaUseTheShortRoute()
    {
        var route = Rules.Route([Item(Rules.PreapprovedItem, "MIC"), Item(Rules.PreviouslyApprovedNpa, "STB")]);
        Assert.Equal(Rules.ShortRoute, route);
    }

    [Fact]
    public void ActAndBimShareOneCap()
    {
        var total = Rules.CombinedAmount(
            [Item(Rules.PreapprovedItem, "ACT", 2, 3_000_000), Item(Rules.PreapprovedItem, "BIM", 1, 2_000_000), Item(Rules.PreapprovedItem, "MIC", 1, 9_000_000)],
            ["ACT", "BIM"]);
        Assert.Equal(8_000_000m, total);
    }

    [Fact]
    public void QuotationCountFollowsTheItemType()
    {
        Assert.Equal(1, Rules.QuotationsRequired(Rules.PreapprovedItem, 1, 3));
        Assert.Equal(3, Rules.QuotationsRequired(Rules.NewNpa, 1, 3));
        Assert.Equal(3, Rules.QuotationsRequired(Rules.PreviouslyApprovedNpa, 1, 3));
    }

    [Fact]
    public void AHigherQuoteNeedsAJustification()
    {
        var problems = Rules.ItemProblems(Item(Rules.NewNpa, "ACT", lowest: false), 3);
        Assert.Contains(problems, problem => problem.Contains("justification"));
    }

    [Fact]
    public void MicAndMpdRequireReview()
    {
        Assert.True(Rules.NeedsReview(["ACT", "MiC"]));
        Assert.False(Rules.NeedsReview(["ACT", "BIM"]));
    }

    [Fact]
    public void FirstInstalmentStopsAtTheConfiguredPercent()
    {
        Assert.True(Rules.WithinFirstInstalment(80_000, 100_000, 80));
        Assert.False(Rules.WithinFirstInstalment(80_001, 100_000, 80));
    }

    [Fact]
    public void FinalInstalmentWaitsForTheConfiguredMonths()
    {
        var first = new DateTime(2026, 1, 15);
        Assert.False(Rules.FinalInstalmentAllowed(first, 12, new DateTime(2026, 12, 15)));
        Assert.True(Rules.FinalInstalmentAllowed(first, 12, new DateTime(2027, 1, 15)));
        Assert.False(Rules.FinalInstalmentAllowed(null, 12, new DateTime(2027, 1, 15)));
    }

    [Fact]
    public void SpotCheckUsesTheReferenceNumber()
    {
        Assert.True(Rules.SelectedForSpotCheck("DR-00010", 10));
        Assert.False(Rules.SelectedForSpotCheck("DR-00011", 10));
        Assert.False(Rules.SelectedForSpotCheck("", 10));
    }

    [Fact]
    public void AssignmentPrefersAMatchingAvailableOfficerWithLessWork()
    {
        var scheme = Guid.NewGuid();
        var busy = Guid.Parse("00000000-0000-0000-0000-0000000000b0");
        var free = Guid.Parse("00000000-0000-0000-0000-0000000000a0");
        var other = Guid.Parse("00000000-0000-0000-0000-0000000000c0");
        var chosen = Rules.ChooseOfficer(
        [
            new OfficerCandidate(busy, scheme, "ACT", 4),
            new OfficerCandidate(free, scheme, "", 1),
            new OfficerCandidate(other, null, "BIM", 0)
        ], [scheme], ["ACT"]);
        Assert.Equal(free, chosen);
    }
}
