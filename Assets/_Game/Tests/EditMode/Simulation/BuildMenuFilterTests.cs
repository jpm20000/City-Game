using System.Collections.Generic;
using NUnit.Framework;

// M25c: the build menu's search and filters (pure).
public sealed class BuildMenuFilterTests
{
    private static BuildMenuEntry E(string id, string name, string group, int age = 1, string effects = "", bool custom = false, bool locked = false)
    {
        return new BuildMenuEntry { Id = id, Name = name, Group = group, Age = age, AgeName = age == 0 ? "Medieval" : age == 1 ? "Renaissance" : "Industrial", Effects = effects, Custom = custom, Locked = locked };
    }

    private static readonly List<BuildMenuEntry> s_Entries = new()
    {
        E("watch_house", "Watch House", "Services", 0, "police order"),
        E("hospital", "Hospital", "Health", 2, "health sick"),
        E("market_hall", "Market Hall", "Services", 1, "jobs 8 shops", custom: true),
        E("academy", "Academy", "Education", 1, "school research", locked: true),
        E("fountain", "Fountain", "Parks", 0, "happiness water"),
    };

    private static string Names(List<int> hits)
    {
        var names = new List<string>();
        foreach (int i in hits) names.Add(s_Entries[i].Id);
        return string.Join(",", names);
    }

    [Test]
    public void EmptyQuery_KeepsDatabaseOrder_WithLockedLast()
    {
        List<int> hits = BuildMenuFilter.Apply(s_Entries, "", null, -1, false, true);
        Assert.AreEqual("watch_house,hospital,market_hall,fountain,academy", Names(hits));
    }

    [Test]
    public void HidingLocked_DropsThem()
    {
        Assert.AreEqual("watch_house,hospital,market_hall,fountain", Names(BuildMenuFilter.Apply(s_Entries, "", null, -1, false, false)));
    }

    [Test]
    public void NamePrefix_BeatsWordStart_BeatsSubstring()
    {
        var entries = new List<BuildMenuEntry>
        {
            E("a", "Grand Hall", "Services"),
            E("b", "Hall of Fame", "Services"),
            E("c", "Marshall", "Services"),
        };
        List<int> hits = BuildMenuFilter.Apply(entries, "hall", null, -1, false, true);
        Assert.AreEqual(new[] { 1, 0, 2 }, hits.ToArray());
    }

    [Test]
    public void EveryWordMustMatch_AndCaseIsIgnored()
    {
        Assert.AreEqual("market_hall", Names(BuildMenuFilter.Apply(s_Entries, "MARKET hall", null, -1, false, true)));
        Assert.AreEqual("", Names(BuildMenuFilter.Apply(s_Entries, "market hospital", null, -1, false, true)));
    }

    [Test]
    public void EffectWords_GroupAndAge_AreSearchable()
    {
        Assert.AreEqual("watch_house", Names(BuildMenuFilter.Apply(s_Entries, "police", null, -1, false, true)));
        Assert.AreEqual("hospital", Names(BuildMenuFilter.Apply(s_Entries, "health", null, -1, false, true)), "name match first? no: group Health, one entry");
        Assert.AreEqual("market_hall,academy", Names(BuildMenuFilter.Apply(s_Entries, "renaissance", null, -1, false, true)));
    }

    [Test]
    public void CustomTag_IsSearchable_AndFiltersCustomOnly()
    {
        Assert.AreEqual("market_hall", Names(BuildMenuFilter.Apply(s_Entries, "custom", null, -1, false, true)));
        Assert.AreEqual("market_hall", Names(BuildMenuFilter.Apply(s_Entries, "", null, -1, true, true)));
    }

    [Test]
    public void GroupAndAgeFilters_Combine()
    {
        Assert.AreEqual("watch_house,market_hall", Names(BuildMenuFilter.Apply(s_Entries, "", "Services", -1, false, true)));
        Assert.AreEqual("market_hall", Names(BuildMenuFilter.Apply(s_Entries, "", "services", 1, false, true)));
        Assert.AreEqual("", Names(BuildMenuFilter.Apply(s_Entries, "", "Parks", 2, false, true)));
    }

    [Test]
    public void NoMatch_ReturnsEmpty()
    {
        Assert.IsEmpty(BuildMenuFilter.Apply(s_Entries, "zzzz", null, -1, false, true));
    }
}
