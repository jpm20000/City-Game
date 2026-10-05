using System.Collections.Generic;
using System.Text;
using NUnit.Framework;
using UnityEngine;

// M19f: the harness player (EngagedCity) plays a 48x48 Medieval city and the tutorial objectives are checked once a day,
// as the game does. The UI-only objectives (Budget panel, an info view) are taken as done by the player the day they
// become current. The tutorial must finish (Renaissance reached) in a sensible time, and no objective may stall.
public sealed class TutorialPlaythroughTests
{
    private BalanceConfig m_Config;

    [SetUp]
    public void SetUp() => m_Config = ScriptableObject.CreateInstance<BalanceConfig>();

    [TearDown]
    public void TearDown() => Object.DestroyImmediate(m_Config);

    [Test]
    public void HarnessPlayer_FinishesTheTutorialWithin120Days()
    {
        var city = new EngagedCity(m_Config, 0, 48);
        city.Sim.ResearchBoost = m_Config.TutorialResearchBoost;   // as SaveGameController does in the tutorial city (M21e)
        var roads = new RoadNetwork(city.Grid);
        var progress = new TutorialProgress();
        progress.Start();
        var days = new List<int>();
        bool budgetOpened = false, viewUsed = false;
        int day = 0;

        for (; day < 200 && !progress.Finished; day++)
        {
            city.RunDay();
            // The player opens the Budget panel / an info view when the card asks.
            if (progress.Active && progress.Current.Id == "books") budgetOpened = true;
            if (progress.Active && progress.Current.Id == "views") viewUsed = true;
            var placed = new List<string>();
            foreach (BuildingRecord record in city.PlacedBuildings()) placed.Add(record.Id);
            TutorialSnapshot snapshot = TutorialSnapshot.Measure(city.Grid, roads, city.Sim, placed, budgetOpened, viewUsed);
            int advanced = progress.Evaluate(snapshot);
            for (int i = 0; i < advanced; i++) days.Add(city.Day);
        }

        var sb = new StringBuilder("Tutorial objective days: ");
        for (int i = 0; i < days.Count; i++) sb.Append($"{TutorialContent.Objectives[i].Id}={days[i]} ");
        TestContext.WriteLine(sb.ToString());
        Assert.IsTrue(progress.Finished, "unfinished at objective " + (progress.Active ? progress.Current.Id : "?") + ". " + sb);
        Assert.LessOrEqual(city.Day, 120, sb.ToString());
    }
}
