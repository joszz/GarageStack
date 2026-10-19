using GarageStack.Core.Helpers;
using GarageStack.Core.Models;

namespace GarageStack.Tests;

// What a lookup keeps of the recalls it saw before, which is what keeps a recall from being
// pushed twice.
public class RdwRecallsTests
{
    private static readonly DateTime Earlier = new(2026, 9, 1, 8, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Now = new(2026, 10, 19, 8, 0, 0, DateTimeKind.Utc);

    private static RdwRecallDetails Details(string code) => new(code, $"defect {code}", $"risk {code}", $"repair {code}", "ERN");

    [Fact]
    public void Merge_NewRecall_IsFirstSeenNow_WithItsDetails()
    {
        var merged = RdwRecalls.Merge([], [new("MGP230061", "O")], [Details("MGP230061")], Now);

        Assert.Equal(
            [new RdwRecall("MGP230061", "O", "defect MGP230061", "risk MGP230061", "repair MGP230061", "ERN", Now)],
            merged);
    }

    [Fact]
    public void Merge_KnownRecall_KeepsWhenItWasSeenAndPushed_AndTakesTheNewStatus()
    {
        var known = new RdwRecall("MGP230061", "O", "defect", "risk", "repair", "ERN", Earlier, NotifiedAt: Earlier);

        var merged = Assert.Single(RdwRecalls.Merge([known], [new("MGP230061", "P")], [], Now));

        Assert.Equal(known with { Status = "P" }, merged);
    }

    [Fact]
    public void Merge_RecallTheRdwNoLongerLists_IsDropped()
    {
        var known = new RdwRecall("MGP230061", "P", null, null, null, null, Earlier);

        Assert.Empty(RdwRecalls.Merge([known], [], [], Now));
    }

    [Fact]
    public void Merge_ListsOpenRecallsFirst()
    {
        var merged = RdwRecalls.Merge(
            [], [new("MGP240278", "O"), new("MGP260056", "P"), new("MGP260068", "O")], [], Now);

        Assert.Equal(["MGP240278", "MGP260068", "MGP260056"], merged.Select(r => r.Code));
    }

    // Only a recall not seen before needs its details asked for.
    [Fact]
    public void Unseen_IsOnlyTheCodesNotKeptYet()
    {
        var known = new RdwRecall("MGP240278", "O", null, null, null, null, Earlier);

        var unseen = RdwRecalls.Unseen([new("MGP240278", "O"), new("MGP260068", "O"), new("MGP260068", "O")], [known]);

        Assert.Equal(["MGP260068"], unseen);
    }

    [Fact]
    public void ToNotify_IsTheOpenRecallsNotPushedYet()
    {
        var recalls = new[]
        {
            new RdwRecall("A1", "O", null, null, null, null, Earlier),
            new RdwRecall("B2", "O", null, null, null, null, Earlier, NotifiedAt: Earlier),
            new RdwRecall("C3", "P", null, null, null, null, Earlier),
        };

        Assert.Equal(["A1"], RdwRecalls.ToNotify(recalls).Select(r => r.Code));
    }

    // The whole life of a recall: pushed once while open, and no news when it is repaired.
    [Fact]
    public void RecallRepairedAfterItsPush_IsNotPushedAgain()
    {
        var first = RdwRecalls.Merge([], [new("MGP230061", "O")], [Details("MGP230061")], Earlier);
        var pushed = first.Select(r => r with { NotifiedAt = Earlier }).ToList();

        var repaired = RdwRecalls.Merge(pushed, [new("MGP230061", "P")], [], Now);
        var openAgain = RdwRecalls.Merge(repaired, [new("MGP230061", "O")], [], Now);

        Assert.Single(RdwRecalls.ToNotify(first));
        Assert.Empty(RdwRecalls.ToNotify(repaired));
        Assert.Empty(RdwRecalls.ToNotify(openAgain));
    }

    [Fact]
    public void Serialize_RoundTrips()
    {
        var recalls = RdwRecalls.Merge([], [new("MGP230061", "O")], [Details("MGP230061")], Now);

        Assert.Equal(recalls, RdwRecalls.Parse(RdwRecalls.Serialize(recalls)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json")]
    public void Parse_Unreadable_IsNoRecalls(string? json)
    {
        Assert.Empty(RdwRecalls.Parse(json));
    }
}
