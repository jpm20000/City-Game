using NUnit.Framework;
using UnityEngine;

public sealed class SimulationTests
{
    private BalanceConfig m_Config;
    private GridData m_Grid;
    private RoadNetwork m_Roads;

    [SetUp]
    public void SetUp()
    {
        m_Config = ScriptableObject.CreateInstance<BalanceConfig>();
        m_Grid = new GridData(24, 24);
        m_Roads = new RoadNetwork(m_Grid);
    }

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(m_Config);
    }

    // Road along row 0 from the map edge; zones on row 1 have road access.
    private void LayRoadRow0(int length)
    {
        for (int x = 0; x < length; x++)
        {
            m_Grid.SetRoad(new Vector2Int(x, 0), true);
        }
    }

    // Mirrors PlacementController.DebugSeedCity.
    private static void SeedCity(GridData grid)
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

    private SimulationSystem RunSeededCity(GridData grid, int days)
    {
        SeedCity(grid);
        SimulationSystem sim = new SimulationSystem(grid, new RoadNetwork(grid), m_Config);
        for (int day = 0; day < days; day++)
        {
            sim.Tick();
        }
        return sim;
    }

    // --- Economy ---

    [Test]
    public void Economy_StartsWithConfigMoneyAndTaxes()
    {
        EconomySystem economy = new EconomySystem(m_Config);

        Assert.AreEqual(50000f, economy.Money);
        Assert.AreEqual(0.10f, economy.TaxResidential, 1e-5f);
    }

    [Test]
    public void Economy_SpendRejectsUnaffordable()
    {
        EconomySystem economy = new EconomySystem(m_Config);

        Assert.IsFalse(economy.Spend(60000f));
        Assert.AreEqual(50000f, economy.Money);
        Assert.IsTrue(economy.Spend(50f));
        Assert.AreEqual(49950f, economy.Money);
    }

    [Test]
    public void Economy_ApplyDay_CanGoNegativeAndRaisesEvent()
    {
        EconomySystem economy = new EconomySystem(m_Config);
        float reported = 0f;
        economy.OnMoneyChanged += m => reported = m;

        economy.ApplyDay(10f, 60010f);

        Assert.AreEqual(-10000f, economy.Money);
        Assert.AreEqual(-10000f, reported);
    }

    // --- Demand ---

    [Test]
    public void Demand_EmptyCity_OnlyResidentialBaseDemand()
    {
        PopulationSystem population = new PopulationSystem(m_Config);
        population.RecountCapacity(m_Grid, default);
        DemandSystem demand = new DemandSystem(m_Config);

        demand.Compute(population);

        Assert.AreEqual(m_Config.ResidentialBaseDemand, demand.ResidentialDemand, 1e-5f);
        Assert.AreEqual(0f, demand.CommercialDemand);
        Assert.AreEqual(0f, demand.IndustrialDemand);
    }

    [Test]
    public void Demand_StaysInUnitRange()
    {
        PopulationSystem population = new PopulationSystem(m_Config);
        population.RecountCapacity(m_Grid, new CityModifiers { CommercialJobs = 500, IndustrialJobs = 500 });
        DemandSystem demand = new DemandSystem(m_Config);

        demand.Compute(population);

        Assert.That(demand.ResidentialDemand, Is.InRange(0f, 1f));
        Assert.That(demand.CommercialDemand, Is.InRange(0f, 1f));
        Assert.That(demand.IndustrialDemand, Is.InRange(0f, 1f));
    }

    // --- Population ---

    [Test]
    public void Population_CountsCapacityFromGrownCellsAndModifiers()
    {
        m_Grid.SetZone(new Vector2Int(3, 3), ZoneType.Residential);
        m_Grid.SetBuildingLevel(new Vector2Int(3, 3), 2);
        m_Grid.SetZone(new Vector2Int(4, 3), ZoneType.Industrial);
        m_Grid.SetBuildingLevel(new Vector2Int(4, 3), 3);
        m_Grid.SetZone(new Vector2Int(5, 3), ZoneType.Commercial); // zoned but undeveloped

        PopulationSystem population = new PopulationSystem(m_Config);
        population.RecountCapacity(m_Grid, new CityModifiers { Housing = 4, CommercialJobs = 2 });

        Assert.AreEqual(8 + 4, population.Housing);
        Assert.AreEqual(16, population.IndustrialJobs);
        Assert.AreEqual(2, population.CommercialJobs);
    }

    [Test]
    public void Population_MovesInUpToHousing_EmployedNeverExceedsWorkers()
    {
        PopulationSystem population = new PopulationSystem(m_Config);
        CityModifiers modifiers = new CityModifiers { Housing = 10, CommercialJobs = 100 };

        for (int i = 0; i < 50; i++)
        {
            population.RecountCapacity(m_Grid, modifiers);
            population.Step(0.10f, 0);
        }

        Assert.AreEqual(10, population.Population);
        Assert.AreEqual(6, population.Workers);
        Assert.AreEqual(6, population.Employed);
        Assert.AreEqual(0, population.Unemployed);
        Assert.AreEqual(m_Config.HappinessBase, population.AverageHappiness, 1e-5f);
    }

    [Test]
    public void Population_LosingHousing_CapsPopulationAndReportsHomeless()
    {
        PopulationSystem population = new PopulationSystem(m_Config);
        for (int i = 0; i < 50; i++)
        {
            population.RecountCapacity(m_Grid, new CityModifiers { Housing = 10 });
            population.Step(0.10f, 0);
        }

        population.RecountCapacity(m_Grid, new CityModifiers { Housing = 4 });
        population.Step(0.10f, 0);

        Assert.AreEqual(4, population.Population);
        Assert.AreEqual(6, population.Homeless);
    }

    // --- Growth ---

    [Test]
    public void Growth_ZonedCellWithRoadAccess_GrowsToLevel1()
    {
        LayRoadRow0(5);
        Vector2Int cell = new Vector2Int(2, 1);
        m_Grid.SetZone(cell, ZoneType.Residential);
        GrowthSystem growth = new GrowthSystem(m_Grid, m_Roads, m_Config);

        growth.Apply(new DemandSnapshot(1f, 0f, 0f));

        Assert.AreEqual(1, m_Grid.GetBuildingLevel(cell));
    }

    [Test]
    public void Growth_NoRoadAccess_DoesNotGrow()
    {
        Vector2Int cell = new Vector2Int(5, 5);
        m_Grid.SetZone(cell, ZoneType.Residential);
        GrowthSystem growth = new GrowthSystem(m_Grid, m_Roads, m_Config);

        growth.Apply(new DemandSnapshot(1f, 1f, 1f));

        Assert.AreEqual(0, m_Grid.GetBuildingLevel(cell));
    }

    [Test]
    public void Growth_DemandAtOrBelowThreshold_DoesNotGrow()
    {
        LayRoadRow0(5);
        Vector2Int cell = new Vector2Int(2, 1);
        m_Grid.SetZone(cell, ZoneType.Residential);
        GrowthSystem growth = new GrowthSystem(m_Grid, m_Roads, m_Config);

        growth.Apply(new DemandSnapshot(m_Config.GrowthDemandThreshold, 0f, 0f));

        Assert.AreEqual(0, m_Grid.GetBuildingLevel(cell));
    }

    [Test]
    public void Growth_RespectsBudget_ThenUpgrades()
    {
        LayRoadRow0(10);
        for (int x = 0; x < 4; x++)
        {
            m_Grid.SetZone(new Vector2Int(x, 1), ZoneType.Industrial);
        }
        GrowthSystem growth = new GrowthSystem(m_Grid, m_Roads, m_Config);
        DemandSnapshot full = new DemandSnapshot(0f, 0f, 1f); // budget = 3

        Assert.AreEqual(3, growth.Apply(full).Count);
        Assert.AreEqual(1, m_Grid.GetBuildingLevel(new Vector2Int(2, 1)));
        Assert.AreEqual(0, m_Grid.GetBuildingLevel(new Vector2Int(3, 1)));

        // Fills the last empty cell first, then upgrades in row-major order.
        growth.Apply(full);
        Assert.AreEqual(1, m_Grid.GetBuildingLevel(new Vector2Int(3, 1)));
        Assert.AreEqual(2, m_Grid.GetBuildingLevel(new Vector2Int(0, 1)));
        Assert.AreEqual(2, m_Grid.GetBuildingLevel(new Vector2Int(1, 1)));
    }

    [Test]
    public void Growth_StopsAtMaxLevel()
    {
        LayRoadRow0(3);
        Vector2Int cell = new Vector2Int(1, 1);
        m_Grid.SetZone(cell, ZoneType.Commercial);
        GrowthSystem growth = new GrowthSystem(m_Grid, m_Roads, m_Config);

        for (int i = 0; i < 10; i++)
        {
            growth.Apply(new DemandSnapshot(0f, 1f, 0f));
        }

        Assert.AreEqual(m_Config.MaxLevel, m_Grid.GetBuildingLevel(cell));
    }

    [Test]
    public void GrownCell_BlocksPlacement()
    {
        Vector2Int cell = new Vector2Int(4, 4);
        m_Grid.SetBuildingLevel(cell, 1);

        Assert.IsFalse(m_Grid.CanPlace(cell, Vector2Int.one, 0));
    }

    // --- Full tick ---

    [Test]
    public void Tick_EmptyCity_ChargesRoadUpkeepOnly()
    {
        LayRoadRow0(5);
        SimulationSystem sim = new SimulationSystem(m_Grid, m_Roads, m_Config);

        sim.Tick();

        Assert.AreEqual(0, sim.Population.Population);
        Assert.AreEqual(5f, sim.Economy.ExpensePerDay, 1e-4f);
        Assert.AreEqual(50000f - 5f, sim.Economy.Money, 1e-2f);
    }

    [Test]
    public void Tick_SeededCity_GrowsWithPositiveCashFlowAfter60Days()
    {
        SimulationSystem sim = RunSeededCity(m_Grid, 60);

        // GamePlan §7 target: ~200-400 population, positive cash flow at 10% tax.
        Assert.That(sim.Population.Population, Is.InRange(150, 400));
        Assert.Greater(sim.Population.Jobs, 0);
        Assert.Greater(sim.Economy.IncomePerDay, sim.Economy.ExpensePerDay);
        Assert.GreaterOrEqual(sim.Population.AverageHappiness, 0.5f);
    }

    [Test]
    public void Tick_IsDeterministic()
    {
        GridData gridB = new GridData(24, 24);
        SimulationSystem a = RunSeededCity(m_Grid, 30);
        SimulationSystem b = RunSeededCity(gridB, 30);

        Assert.AreEqual(a.Population.Population, b.Population.Population);
        Assert.AreEqual(a.Economy.Money, b.Economy.Money);
        for (int y = 0; y < 24; y++)
        {
            for (int x = 0; x < 24; x++)
            {
                Vector2Int cell = new Vector2Int(x, y);
                Assert.AreEqual(m_Grid.GetBuildingLevel(cell), gridB.GetBuildingLevel(cell));
            }
        }
    }
}
