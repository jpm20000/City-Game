using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

// The seeded test city shared by the simulation, save and age tests. Mirrors
// PlacementController.DebugSeedCity: a road cross through the middle with R/C/I strips beside it,
// a 3x3 power plant in the west commercial strip touching the east-west road, and two parks
// placed to cover most of the homes.
internal static class SeededCity
{
    public static readonly Vector2Int PlantOrigin = new Vector2Int(0, 9);
    public const int PlantSupply = 600;
    public static readonly Vector2Int[] ParkOrigins = { new Vector2Int(5, 14), new Vector2Int(14, 16) };

    public static void Seed(GridData grid)
    {
        int mid = grid.Width / 2;
        for (int i = 0; i < grid.Width; i++)
        {
            grid.SetRoad(new Vector2Int(i, mid), true);
            grid.SetRoad(new Vector2Int(mid, i), true);
        }
        for (int i = 0; i < grid.Width; i++)
        {
            SeedZone(grid, new Vector2Int(i, mid + 1), ZoneType.Residential);
            SeedZone(grid, new Vector2Int(i, mid - 1), i < mid ? ZoneType.Commercial : ZoneType.Industrial);
            SeedZone(grid, new Vector2Int(mid - 1, i), i > mid ? ZoneType.Residential : ZoneType.Commercial);
            SeedZone(grid, new Vector2Int(mid + 1, i), i > mid ? ZoneType.Residential : ZoneType.Industrial);
        }
    }

    private static void SeedZone(GridData grid, Vector2Int cell, ZoneType zone)
    {
        if (!grid.InBounds(cell) || grid.IsRoad(cell) || grid.GetZone(cell) != ZoneType.None) return;
        grid.SetZone(cell, zone);
    }

    // plantSupply 0 = no power plant. Upkeep follows §7 ($100/day plant, $5/day park). With ages,
    // the city starts in startAge (StartNew) before the first tick.
    public static SimulationSystem Build(GridData grid, BalanceConfig config, int plantSupply = PlantSupply,
        bool parks = false, float jobTax = 0.10f, AgeDatabase ages = null, TechDatabase techs = null, int startAge = -1)
    {
        Seed(grid);
        var sources = new List<ServiceSource>();
        CityModifiers modifiers = default;
        if (plantSupply > 0)
        {
            PlaceSource(grid, sources, ref modifiers, PlantOrigin, new Vector2Int(3, 3), 0, plantSupply, 100f);
        }
        if (parks)
        {
            foreach (Vector2Int origin in ParkOrigins)
            {
                PlaceSource(grid, sources, ref modifiers, origin, new Vector2Int(2, 2), 4, 0, 5f);
            }
        }

        var sim = new SimulationSystem(grid, new RoadNetwork(grid), config, ages, techs);
        if (sim.Tech != null && startAge >= 0) sim.Tech.StartNew(startAge);
        sim.Sources = sources;
        sim.Modifiers = modifiers;
        sim.Economy.TaxCommercial = jobTax;
        sim.Economy.TaxIndustrial = jobTax;
        return sim;
    }

    public static SimulationSystem Run(GridData grid, BalanceConfig config, int days, int plantSupply = PlantSupply,
        bool parks = false, float jobTax = 0.10f, AgeDatabase ages = null, TechDatabase techs = null, int startAge = -1)
    {
        SimulationSystem sim = Build(grid, config, plantSupply, parks, jobTax, ages, techs, startAge);
        for (int day = 0; day < days; day++) sim.Tick();
        return sim;
    }

    private static void PlaceSource(GridData grid, List<ServiceSource> sources, ref CityModifiers modifiers,
        Vector2Int origin, Vector2Int size, int radius, int supply, float upkeep)
    {
        foreach (Vector2Int cell in grid.GetFootprint(origin, size, 0))
        {
            grid.SetZone(cell, ZoneType.None);
        }
        Assert.IsTrue(grid.Occupy(origin, size, 0, sources.Count + 1));
        sources.Add(new ServiceSource(origin, size, radius, supply));
        modifiers.UpkeepPerDay += upkeep;
    }
}
