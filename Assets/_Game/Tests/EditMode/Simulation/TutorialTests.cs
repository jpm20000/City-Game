using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

// M19f: the tutorial's objectives, each condition on a hand-made snapshot, the sequential advance, and the snapshot
// measured from a small built grid.
public sealed class TutorialTests
{
    private static TutorialSnapshot Snap() => new TutorialSnapshot();

    [Test]
    public void Objectives_TwelveWithTextAndNoDuplicateIds()
    {
        Assert.AreEqual(12, TutorialContent.Count);
        var ids = new HashSet<string>();
        foreach (TutorialObjective o in TutorialContent.Objectives)
        {
            Assert.IsFalse(string.IsNullOrEmpty(o.Title), o.Id);
            Assert.IsFalse(string.IsNullOrEmpty(o.Body), o.Id);
            Assert.IsTrue(ids.Add(o.Id), "duplicate id " + o.Id);
        }
    }

    [Test]
    public void Conditions_EachMetOnlyWhenItsNumbersAre()
    {
        TutorialObjective[] o = TutorialContent.Objectives;
        var s = Snap();
        foreach (TutorialObjective objective in o) Assert.IsFalse(objective.IsMet(s), objective.Id + " met on an empty city");

        s.ConnectedRoads = 7;
        Assert.IsFalse(o[0].IsMet(s));
        s.ConnectedRoads = 8;
        Assert.IsTrue(o[0].IsMet(s));

        s.ResidentialCells = 6;
        Assert.IsTrue(o[1].IsMet(s));
        s.Placed["well"] = 1;
        Assert.IsTrue(o[2].IsMet(s));
        s.Population = 10;
        Assert.IsTrue(o[3].IsMet(s));

        s.CommercialCells = 9;   // capped at 4, so shops alone do not do it
        Assert.IsFalse(o[4].IsMet(s));
        s.IndustrialCells = 3;
        Assert.IsFalse(o[4].IsMet(s));
        s.IndustrialCells = 4;
        Assert.IsTrue(o[4].IsMet(s));

        s.BudgetOpened = true;
        Assert.IsTrue(o[5].IsMet(s));
        s.ResearchActive = true;
        Assert.IsTrue(o[6].IsMet(s));

        s.Techs.Add("commons");
        Assert.IsFalse(o[7].IsMet(s), "Commons alone is not enough");
        s.Placed["park"] = 1;
        Assert.IsTrue(o[7].IsMet(s));

        s.ViewUsed = true;
        Assert.IsTrue(o[8].IsMet(s));
        s.Population = 59;
        Assert.IsFalse(o[9].IsMet(s));
        s.Population = 60;
        Assert.IsTrue(o[9].IsMet(s));
        s.Placed["monastery"] = 1;
        Assert.IsTrue(o[10].IsMet(s));
        s.Age = 1;
        Assert.IsTrue(o[11].IsMet(s));
    }

    [Test]
    public void Progress_AdvancesInOrder_AndAMetObjectiveCompletesAtOnce()
    {
        var progress = new TutorialProgress();
        progress.Start();
        var s = Snap();
        Assert.AreEqual(0, progress.Evaluate(s));
        Assert.AreEqual(0, progress.Index);

        // Objective 3 (the well) is met first, but order holds: it only counts once 1 and 2 are done.
        s.Placed["well"] = 1;
        Assert.AreEqual(0, progress.Evaluate(s));
        s.ConnectedRoads = 8;
        s.ResidentialCells = 6;
        Assert.AreEqual(3, progress.Evaluate(s), "roads, homes and the already-placed well complete together");
        Assert.AreEqual(3, progress.Index);
        Assert.AreEqual("villagers", progress.Current.Id);
    }

    [Test]
    public void Progress_FinishesSkipsAndRestores()
    {
        var progress = new TutorialProgress();
        Assert.IsFalse(progress.Active);
        Assert.IsFalse(progress.Finished);

        progress.Restore(5);
        Assert.IsTrue(progress.Active);
        Assert.AreEqual("books", progress.Current.Id);
        progress.Restore(99);
        Assert.IsTrue(progress.Finished);
        Assert.IsFalse(progress.Active);
        progress.Restore(-3);
        Assert.AreEqual(TutorialProgress.None, progress.Index);

        progress.Start();
        progress.Skip();
        Assert.IsFalse(progress.Active);
        Assert.IsFalse(progress.Finished);
    }

    [Test]
    public void Progress_IndexSurvivesASaveRoundTrip()
    {
        SaveData data = new SaveData { Tutorial = 7 };
        string json = JsonUtility.ToJson(data);
        SaveData back = JsonUtility.FromJson<SaveData>(json);
        var progress = new TutorialProgress();
        progress.Restore(back.Tutorial);
        Assert.AreEqual(7, progress.Index);
        Assert.AreEqual("green", progress.Current.Id);
    }

    [Test]
    public void Progress_Text_ShowsDoneOverNeededWhenThereIsMoreThanOne()
    {
        var s = Snap();
        s.ConnectedRoads = 3;
        Assert.AreEqual("3 / 8 road cells", TutorialContent.Objectives[0].Progress(s));
        Assert.AreEqual("", TutorialContent.Objectives[5].Progress(s));
        s.ConnectedRoads = 50;
        Assert.AreEqual("8 / 8 road cells", TutorialContent.Objectives[0].Progress(s));
    }

    [Test]
    public void Measure_CountsRoadsZonesAndPlacedBuildings_OnABuiltGrid()
    {
        var config = ScriptableObject.CreateInstance<BalanceConfig>();
        try
        {
            var grid = new GridData(16, 16);
            var roads = new RoadNetwork(grid);
            SimulationSystem sim = new SimulationSystem(grid, roads, config);
            for (int x = 0; x < 6; x++) grid.SetRoad(new Vector2Int(x, 5), true);          // from the west edge
            for (int x = 10; x < 13; x++) grid.SetRoad(new Vector2Int(x, 9), true);        // an island, not on the edge
            for (int x = 0; x < 4; x++) grid.SetZone(new Vector2Int(x, 6), ZoneType.Residential);   // beside the road
            grid.SetZone(new Vector2Int(2, 12), ZoneType.Residential);                    // no road access
            grid.SetZone(new Vector2Int(1, 4), ZoneType.Commercial);
            grid.SetZone(new Vector2Int(8, 8), ZoneType.Industrial);

            TutorialSnapshot s = TutorialSnapshot.Measure(grid, roads, sim, new[] { "well", "well", "park" }, true, false);
            Assert.AreEqual(6, s.ConnectedRoads);
            Assert.AreEqual(4, s.ResidentialCells);
            Assert.AreEqual(1, s.CommercialCells);
            Assert.AreEqual(1, s.IndustrialCells);
            Assert.AreEqual(2, s.PlacedCount("well"));
            Assert.AreEqual(1, s.PlacedCount("park"));
            Assert.AreEqual(0, s.PlacedCount("monastery"));
            Assert.IsTrue(s.BudgetOpened);
            Assert.IsFalse(s.ViewUsed);
        }
        finally
        {
            Object.DestroyImmediate(config);
        }
    }
}
