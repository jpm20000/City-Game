using System.Collections.Generic;
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

    // plantSupply 0 = no power plant. See SeededCity.
    private SimulationSystem RunSeededCity(GridData grid, int days, int plantSupply = SeededCity.PlantSupply,
        bool parks = false, float jobTax = 0.10f)
    {
        return SeededCity.Run(grid, m_Config, days, plantSupply, parks, jobTax);
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

        demand.Compute(population, 0.10f, 0.10f, 0.10f);

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

        demand.Compute(population, 0.10f, 0.10f, 0.10f);

        Assert.That(demand.ResidentialDemand, Is.InRange(0f, 1f));
        Assert.That(demand.CommercialDemand, Is.InRange(0f, 1f));
        Assert.That(demand.IndustrialDemand, Is.InRange(0f, 1f));
    }

    [Test]
    public void Demand_TaxAboveThresholdLowersDemand_BelowRaisesIt()
    {
        // Small town so no demand is clamped at 1 and the multiplier stays visible.
        PopulationSystem population = new PopulationSystem(m_Config);
        population.RecountCapacity(m_Grid, new CityModifiers { Housing = 10 });
        for (int i = 0; i < 30; i++) population.Step(0.10f, 0.10f, 0.10f, default);
        population.RecountCapacity(m_Grid, new CityModifiers { Housing = 10 });
        DemandSystem demand = new DemandSystem(m_Config);

        demand.Compute(population, 0.10f, 0.10f, 0.10f);
        DemandSnapshot neutral = demand.Snapshot;
        demand.Compute(population, 0.20f, 0.20f, 0.20f);
        DemandSnapshot high = demand.Snapshot;
        demand.Compute(population, 0.05f, 0.05f, 0.05f);
        DemandSnapshot low = demand.Snapshot;

        Assert.Greater(neutral.Commercial, 0f);
        Assert.Less(high.Residential, neutral.Residential);
        Assert.Less(high.Commercial, neutral.Commercial);
        Assert.Less(high.Industrial, neutral.Industrial);
        Assert.GreaterOrEqual(low.Commercial, neutral.Commercial);
        Assert.AreEqual(1f, demand.TaxMultiplier(m_Config.TaxPenaltyThreshold), 1e-5f);
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
            population.Step(0.10f, 0.10f, 0.10f, default);
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
        // Jobs keep the city content; with none, unemployment would drive residents out first.
        PopulationSystem population = new PopulationSystem(m_Config);
        for (int i = 0; i < 50; i++)
        {
            population.RecountCapacity(m_Grid, new CityModifiers { Housing = 10, CommercialJobs = 100 });
            population.Step(0.10f, 0.10f, 0.10f, default);
        }

        population.RecountCapacity(m_Grid, new CityModifiers { Housing = 4, CommercialJobs = 100 });
        population.Step(0.10f, 0.10f, 0.10f, default);

        Assert.AreEqual(4, population.Population);
        Assert.AreEqual(6, population.Homeless);
    }

    // Fills the given housing over 60 steps with fixed capacity and taxes.
    private PopulationSystem StepCity(CityModifiers modifiers, float taxR = 0.10f, float taxC = 0.10f, float taxI = 0.10f,
        ServiceStats services = default)
    {
        PopulationSystem population = new PopulationSystem(m_Config);
        for (int i = 0; i < 60; i++)
        {
            population.RecountCapacity(m_Grid, modifiers);
            population.Step(taxR, taxC, taxI, services);
        }
        return population;
    }

    [Test]
    public void Population_UnhappyCity_LosesResidents()
    {
        // 100 homes, no jobs: once past the small-town grace, unemployment sinks happiness.
        PopulationSystem population = StepCity(new CityModifiers { Housing = 100 });

        Assert.Less(population.AverageHappiness, m_Config.LowHappinessThreshold);
        Assert.Greater(population.MovedOut, 0);
        Assert.Less(population.Population, population.Housing);
    }

    [Test]
    public void Population_ContentCity_KeepsResidents()
    {
        PopulationSystem population = StepCity(new CityModifiers { Housing = 100, CommercialJobs = 100 });

        Assert.AreEqual(0, population.MovedOut);
        Assert.AreEqual(100, population.Population);
    }

    [Test]
    public void Happiness_SmallTown_IgnoresUnemploymentAndPollution()
    {
        // 10 residents, no jobs at all: well inside the grace population.
        PopulationSystem population = StepCity(new CityModifiers { Housing = 10 });
        Assert.Less(population.Population, m_Config.SmallTownGracePopulation);

        float weight = (float)population.Population / m_Config.SmallTownGracePopulation;
        Assert.AreEqual(m_Config.HappinessBase - m_Config.UnemploymentPenalty * weight, population.AverageHappiness, 1e-4f);
        Assert.GreaterOrEqual(population.AverageHappiness, m_Config.LowHappinessThreshold);
    }

    [Test]
    public void Happiness_Pollution_IndustryHurtsMoreThanCommerce()
    {
        // Same jobs and homes; industry's share of development is 40 / 200 in the mixed city.
        PopulationSystem commerce = StepCity(new CityModifiers { Housing = 100, CommercialJobs = 100 });
        PopulationSystem mixed = StepCity(new CityModifiers { Housing = 100, CommercialJobs = 60, IndustrialJobs = 40 });

        Assert.AreEqual(commerce.Employed, mixed.Employed);
        Assert.AreEqual(m_Config.PollutionPenalty * 0.2f, commerce.AverageHappiness - mixed.AverageHappiness, 1e-4f);
    }

    [Test]
    public void Happiness_TaxesCostAndParksHelp()
    {
        CityModifiers city = new CityModifiers { Housing = 100, CommercialJobs = 100 };
        float neutral = StepCity(city).AverageHappiness;
        float jobTaxes = StepCity(city, 0.10f, 0.20f, 0.20f).AverageHappiness;
        float parks = StepCity(city, services: new ServiceStats(0.10f, 0f)).AverageHappiness;
        float blackout = StepCity(city, services: new ServiceStats(0f, 1f)).AverageHappiness;

        Assert.AreEqual(m_Config.JobTaxPenalty * 0.20f, neutral - jobTaxes, 1e-4f);
        Assert.AreEqual(0.10f, parks - neutral, 1e-4f);
        Assert.AreEqual(m_Config.PowerPenalty, neutral - blackout, 1e-4f);
    }

    [Test]
    public void Happiness_BreakdownExplainsTotal()
    {
        CityModifiers city = new CityModifiers { Housing = 100, CommercialJobs = 30, IndustrialJobs = 20 };
        PopulationSystem population = StepCity(city, 0.12f, 0.15f, 0.10f, new ServiceStats(0.05f, 0.5f));
        HappinessBreakdown h = population.Happiness;

        Assert.AreEqual(population.AverageHappiness, h.Total, 1e-5f);
        Assert.AreEqual(m_Config.HappinessBase, h.Base, 1e-5f);
        Assert.Less(h.Unemployment, 0f);    // 60 workers, 50 jobs
        Assert.AreEqual(-(m_Config.TaxPenalty * 0.02f + m_Config.JobTaxPenalty * 0.05f), h.Taxes, 1e-4f);
        Assert.AreEqual(-m_Config.PollutionPenalty * 20f / 150f, h.Pollution, 1e-4f);
        Assert.AreEqual(0.05f, h.Services, 1e-5f);
        Assert.AreEqual(-m_Config.PowerPenalty * 0.5f, h.Power, 1e-5f);
    }

    // --- Growth ---

    // A plant with ample supply standing on row 1 at `plantX`, fed by the row-0 road.
    private GrowthSystem MakeGrowth(int plantX = -1)
    {
        PowerSystem power = new PowerSystem(m_Grid, m_Config);
        if (plantX >= 0)
        {
            power.SetSources(new[] { new ServiceSource(new Vector2Int(plantX, 1), Vector2Int.one, 0, 1000) });
        }
        return new GrowthSystem(m_Grid, m_Roads, power, m_Config);
    }

    [Test]
    public void Growth_ZonedCellWithRoadAccess_GrowsToLevel1()
    {
        LayRoadRow0(5);
        Vector2Int cell = new Vector2Int(2, 1);
        m_Grid.SetZone(cell, ZoneType.Residential);
        GrowthSystem growth = MakeGrowth();

        growth.Apply(new DemandSnapshot(1f, 0f, 0f));

        Assert.AreEqual(1, m_Grid.GetBuildingLevel(cell));
    }

    [Test]
    public void Growth_NoRoadAccess_DoesNotGrow()
    {
        Vector2Int cell = new Vector2Int(5, 5);
        m_Grid.SetZone(cell, ZoneType.Residential);
        GrowthSystem growth = MakeGrowth();

        growth.Apply(new DemandSnapshot(1f, 1f, 1f));

        Assert.AreEqual(0, m_Grid.GetBuildingLevel(cell));
    }

    [Test]
    public void Growth_DemandAtOrBelowThreshold_DoesNotGrow()
    {
        LayRoadRow0(5);
        Vector2Int cell = new Vector2Int(2, 1);
        m_Grid.SetZone(cell, ZoneType.Residential);
        GrowthSystem growth = MakeGrowth();

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
        GrowthSystem growth = MakeGrowth(plantX: 8);
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
        GrowthSystem growth = MakeGrowth(plantX: 2);

        for (int i = 0; i < 10; i++)
        {
            growth.Apply(new DemandSnapshot(0f, 1f, 0f));
        }

        Assert.AreEqual(m_Config.MaxLevel, m_Grid.GetBuildingLevel(cell));
    }

    [Test]
    public void GetBlocker_ReportsEachReason()
    {
        LayRoadRow0(5);
        GrowthSystem growth = MakeGrowth();
        DemandSnapshot high = new DemandSnapshot(1f, 1f, 1f);

        Assert.AreEqual(GrowthBlocker.NotZoned, growth.GetBlocker(new Vector2Int(2, 1), high));
        Assert.AreEqual(GrowthBlocker.NotZoned, growth.GetBlocker(new Vector2Int(2, 0), high)); // road

        Vector2Int far = new Vector2Int(10, 10);
        m_Grid.SetZone(far, ZoneType.Residential);
        Assert.AreEqual(GrowthBlocker.NoRoadAccess, growth.GetBlocker(far, high));

        Vector2Int near = new Vector2Int(2, 1);
        m_Grid.SetZone(near, ZoneType.Commercial);
        Assert.AreEqual(GrowthBlocker.LowDemand, growth.GetBlocker(near, new DemandSnapshot(1f, 0.1f, 1f)));
        Assert.AreEqual(GrowthBlocker.None, growth.GetBlocker(near, high));

        m_Grid.SetBuildingLevel(near, (byte)m_Config.MaxLevel);
        Assert.AreEqual(GrowthBlocker.MaxLevel, growth.GetBlocker(near, high));

        Vector2Int occupied = new Vector2Int(3, 1);
        m_Grid.SetZone(occupied, ZoneType.Industrial);
        m_Grid.Occupy(occupied, Vector2Int.one, 0, 42);
        Assert.AreEqual(GrowthBlocker.Occupied, growth.GetBlocker(occupied, high));
    }

    [Test]
    public void GetBlocker_None_MatchesApply()
    {
        LayRoadRow0(5);
        Vector2Int cell = new Vector2Int(2, 1);
        m_Grid.SetZone(cell, ZoneType.Residential);
        GrowthSystem growth = MakeGrowth();
        DemandSnapshot demand = new DemandSnapshot(0.5f, 0f, 0f);

        Assert.AreEqual(GrowthBlocker.None, growth.GetBlocker(cell, demand));
        growth.Apply(demand);
        Assert.AreEqual(1, m_Grid.GetBuildingLevel(cell));
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
    public void Tick_SeededCity_WithoutPower_StaysAtLevel1()
    {
        SimulationSystem sim = RunSeededCity(m_Grid, 60, plantSupply: 0);

        Assert.Greater(sim.Population.Population, 0);
        for (int y = 0; y < 24; y++)
        {
            for (int x = 0; x < 24; x++)
            {
                Assert.LessOrEqual(m_Grid.GetBuildingLevel(new Vector2Int(x, y)), 1);
            }
        }
        Assert.AreEqual(1f, sim.MeasureServices().UnpoweredHousingShare, 1e-5f);
        Assert.Less(sim.Population.Happiness.Power, 0f);
    }

    [Test]
    public void Tick_SeededCity_WithoutPower_StaysContent_PlantPaysOff()
    {
        // The pressure to build a plant is being stuck at level 1, not residents leaving.
        GridData poweredGrid = new GridData(24, 24);
        SimulationSystem unpowered = RunSeededCity(m_Grid, 90, plantSupply: 0);
        SimulationSystem powered = RunSeededCity(poweredGrid, 90);

        Assert.GreaterOrEqual(unpowered.Population.AverageHappiness, m_Config.LowHappinessThreshold);
        Assert.AreEqual(0, unpowered.Population.MovedOut);
        Assert.Greater(powered.Population.Population, unpowered.Population.Population * 3 / 2);
        Assert.Greater(powered.Economy.IncomePerDay - powered.Economy.ExpensePerDay,
            unpowered.Economy.IncomePerDay - unpowered.Economy.ExpensePerDay);
    }

    [Test]
    public void Tick_SeededCity_ParksRaiseHappinessOnlyWhereTheyReach()
    {
        GridData parkGrid = new GridData(24, 24);
        SimulationSystem plain = RunSeededCity(m_Grid, 60);
        SimulationSystem parks = RunSeededCity(parkGrid, 60, parks: true);

        float bonus = parks.Population.Happiness.Services;
        Assert.Greater(bonus, 0.05f);                                  // most homes are covered...
        Assert.Less(bonus, 2 * m_Config.ServiceBonusEach);              // ...but not all by both parks
        Assert.AreEqual(bonus, parks.Population.AverageHappiness - plain.Population.AverageHappiness, 0.01f);
    }

    [Test]
    public void Tick_SeededCity_ParksRescueHighJobTaxes()
    {
        GridData parkGrid = new GridData(24, 24);
        SimulationSystem taxed = RunSeededCity(m_Grid, 60, jobTax: 0.20f);
        SimulationSystem taxedWithParks = RunSeededCity(parkGrid, 60, parks: true, jobTax: 0.20f);

        Assert.Less(taxed.Population.AverageHappiness, m_Config.LowHappinessThreshold + 0.005f);
        Assert.GreaterOrEqual(taxedWithParks.Population.AverageHappiness, m_Config.LowHappinessThreshold);
        Assert.Greater(taxedWithParks.Population.Population, taxed.Population.Population * 2);
    }

    [Test]
    public void Tick_SeededCity_LoadNeverExceedsSupply()
    {
        SimulationSystem sim = RunSeededCity(m_Grid, 60, plantSupply: 200);

        Assert.Greater(sim.Power.Demand, 200);                // the city outgrew its plant...
        Assert.LessOrEqual(sim.Power.Load, sim.Power.Supply); // ...without overdrawing it
    }

    [Test]
    public void Tick_SeededCity_HighJobTaxesStallGrowth()
    {
        SimulationSystem normal = RunSeededCity(m_Grid, 60);
        SimulationSystem taxed = RunSeededCity(new GridData(24, 24), 60, jobTax: 0.20f);

        // Without parks, 20% C/I taxes leave the city unhappy and far smaller.
        Assert.Less(taxed.Population.Jobs, normal.Population.Jobs);
        Assert.Less(taxed.Population.Population, normal.Population.Population / 2);
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
