using System.Collections.Generic;
using UnityEngine;

// What the tutorial reads of the city (M19f): a handful of counts, taken once per check. Read-only: it never touches
// the sim's state, so the tutorial cannot change a run.
public sealed class TutorialSnapshot
{
    public int ConnectedRoads;       // road cells connected to the map edge
    public int ResidentialCells;     // residential cells that have road access
    public int CommercialCells;
    public int IndustrialCells;
    public int Population;
    public int Age;
    public bool ResearchActive;
    public bool BudgetOpened;        // UI flags the runtime sets
    public bool ViewUsed;
    public readonly HashSet<string> Techs = new();
    public readonly Dictionary<string, int> Placed = new();

    public int PlacedCount(string buildingId) => Placed.TryGetValue(buildingId, out int count) ? count : 0;
    public bool Has(string techId) => Techs.Contains(techId);

    public static TutorialSnapshot Measure(GridData grid, RoadNetwork roads, SimulationSystem sim, IEnumerable<string> placedIds,
        bool budgetOpened, bool viewUsed)
    {
        var snapshot = new TutorialSnapshot { BudgetOpened = budgetOpened, ViewUsed = viewUsed };
        for (int y = 0; y < grid.Height; y++)
        {
            for (int x = 0; x < grid.Width; x++)
            {
                var cell = new Vector2Int(x, y);
                if (grid.IsRoad(cell))
                {
                    if (roads.IsConnectedToEntry(cell)) snapshot.ConnectedRoads++;
                    continue;
                }
                switch (grid.GetZone(cell))
                {
                    case ZoneType.Residential:
                        if (roads.HasRoadAccess(cell)) snapshot.ResidentialCells++;
                        break;
                    case ZoneType.Commercial: snapshot.CommercialCells++; break;
                    case ZoneType.Industrial: snapshot.IndustrialCells++; break;
                }
            }
        }

        snapshot.Population = sim.Population.Population;
        if (sim.Tech != null)
        {
            snapshot.Age = sim.Tech.CurrentAge;
            snapshot.ResearchActive = sim.Tech.Active != null;
            foreach (TechDefinition tech in sim.Tech.Researched) snapshot.Techs.Add(tech.Id);
        }
        if (placedIds != null)
        {
            foreach (string id in placedIds) snapshot.Placed[id] = snapshot.PlacedCount(id) + 1;
        }
        return snapshot;
    }
}
