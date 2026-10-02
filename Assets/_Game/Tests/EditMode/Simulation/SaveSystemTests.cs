using System.IO;
using NUnit.Framework;
using UnityEngine;

public sealed class SaveSystemTests
{
    private BalanceConfig m_Config;
    private string m_TempDir;

    [SetUp]
    public void SetUp()
    {
        m_Config = ScriptableObject.CreateInstance<BalanceConfig>();
        m_TempDir = Path.Combine(Path.GetTempPath(), "CityBuilderSaveTests_" + System.Guid.NewGuid().ToString("N"));
    }

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(m_Config);
        if (Directory.Exists(m_TempDir)) Directory.Delete(m_TempDir, true);
    }

    private static void Run(SimulationSystem sim, int days)
    {
        for (int day = 0; day < days; day++) sim.Tick();
    }

    // Save -> JSON -> load into fresh systems, the same steps SaveGameController takes.
    private SimulationSystem LoadIntoNew(SaveData saved, out GridData grid, ServiceSource[] sources = null)
    {
        Assert.IsTrue(SaveSystem.TryFromJson(SaveSystem.ToJson(saved), out SaveData loaded, out string error), error);

        grid = new GridData(loaded.Width, loaded.Height);
        SimulationSystem sim = new SimulationSystem(grid, new RoadNetwork(grid), m_Config);
        SaveSystem.ApplyGrid(loaded, grid);
        if (sources != null) sim.Sources = sources;   // SaveGameController re-places buildings here
        SaveSystem.ApplySimulation(loaded, sim);
        return sim;
    }

    private static void AssertSameGrid(GridData expected, GridData actual)
    {
        for (int y = 0; y < expected.Height; y++)
        {
            for (int x = 0; x < expected.Width; x++)
            {
                Vector2Int cell = new Vector2Int(x, y);
                Assert.AreEqual(expected.GetZone(cell), actual.GetZone(cell), $"zone {cell}");
                Assert.AreEqual(expected.IsRoad(cell), actual.IsRoad(cell), $"road {cell}");
                Assert.AreEqual(expected.GetBuildingLevel(cell), actual.GetBuildingLevel(cell), $"level {cell}");
            }
        }
    }

    // --- GridData export / import ---

    [Test]
    public void Grid_ExportImport_RoundTripsAndRaisesChangedCells()
    {
        GridData source = new GridData(8, 6);
        source.SetRoad(new Vector2Int(0, 0), true);
        source.SetZone(new Vector2Int(1, 0), ZoneType.Commercial);
        source.SetZone(new Vector2Int(7, 5), ZoneType.Industrial);
        source.SetBuildingLevel(new Vector2Int(7, 5), 3);

        GridData target = new GridData(8, 6);
        target.SetZone(new Vector2Int(4, 4), ZoneType.Residential); // must be cleared by the import
        int changed = 0;
        target.OnCellChanged += _ => changed++;

        target.Import(source.ExportZones(), source.ExportRoads(), source.ExportLevels());

        AssertSameGrid(source, target);
        Assert.AreEqual(4, changed);
    }

    [Test]
    public void Grid_Import_ClearsOccupancy()
    {
        GridData grid = new GridData(4, 4);
        grid.Occupy(new Vector2Int(1, 1), new Vector2Int(2, 2), 0, 7);

        grid.Import(new byte[16], new byte[16], new byte[16]);

        Assert.IsFalse(grid.IsOccupied(new Vector2Int(1, 1)));
        Assert.IsFalse(grid.IsOccupied(new Vector2Int(2, 2)));
    }

    [Test]
    public void Grid_Import_RejectsWrongSize()
    {
        GridData grid = new GridData(4, 4);
        Assert.Throws<System.ArgumentException>(() => grid.Import(new byte[15], new byte[16], new byte[16]));
    }

    // --- Capture / apply ---

    [Test]
    public void Capture_RoundTripsEconomyPopulationAndGrid()
    {
        GridData grid = new GridData(24, 24);
        SeededCity.Seed(grid);
        SimulationSystem sim = new SimulationSystem(grid, new RoadNetwork(grid), m_Config);
        Run(sim, 30);
        sim.Economy.TaxResidential = 0.13f;
        sim.Economy.TaxIndustrial = 0.07f;

        SimulationSystem loaded = LoadIntoNew(SaveSystem.Capture(grid, sim), out GridData loadedGrid);

        AssertSameGrid(grid, loadedGrid);
        Assert.AreEqual(sim.Economy.Money, loaded.Economy.Money);
        Assert.AreEqual(sim.Economy.IncomePerDay, loaded.Economy.IncomePerDay);
        Assert.AreEqual(sim.Economy.ExpensePerDay, loaded.Economy.ExpensePerDay);
        Assert.AreEqual(0.13f, loaded.Economy.TaxResidential);
        Assert.AreEqual(0.07f, loaded.Economy.TaxIndustrial);
        Assert.AreEqual(sim.Population.Population, loaded.Population.Population);
        Assert.AreEqual(sim.Population.AverageHappiness, loaded.Population.AverageHappiness);
        // Derived stats are recomputed on restore, not left at zero until the next tick.
        Assert.AreEqual(sim.Population.Housing, loaded.Population.Housing);
        Assert.AreEqual(sim.Population.Jobs, loaded.Population.Jobs);
        Assert.AreEqual(sim.Population.Employed, loaded.Population.Employed);
        // The HUD tooltip's breakdown is rebuilt too (taxes changed after the last tick, so compare terms).
        Assert.AreEqual(sim.Population.Happiness.Pollution, loaded.Population.Happiness.Pollution, 1e-5f);
        Assert.AreEqual(sim.Population.Happiness.Unemployment, loaded.Population.Happiness.Unemployment, 1e-5f);
        Assert.Less(loaded.Population.Happiness.Taxes, 0f);
    }

    [Test]
    public void SaveMidGame_ThenContinue_MatchesUninterruptedRun()
    {
        GridData gridA = new GridData(24, 24);
        SeededCity.Seed(gridA);
        SimulationSystem a = new SimulationSystem(gridA, new RoadNetwork(gridA), m_Config);
        Run(a, 30);

        SimulationSystem b = LoadIntoNew(SaveSystem.Capture(gridA, a), out GridData gridB);

        Run(a, 30);
        Run(b, 30);

        Assert.AreEqual(a.Population.Population, b.Population.Population);
        Assert.AreEqual(a.Population.AverageHappiness, b.Population.AverageHappiness);
        Assert.AreEqual(a.Economy.Money, b.Economy.Money);
        AssertSameGrid(gridA, gridB);
    }

    [Test]
    public void SaveWithPlantAndPark_ThenContinue_MatchesUninterruptedRun()
    {
        // Plant (west C strip, on the E-W road) and park; power and coverage aren't saved, they are
        // derived from the re-placed buildings, so a different re-placement order must not matter.
        ServiceSource plant = new ServiceSource(new Vector2Int(0, 9), new Vector2Int(3, 3), 0, 400);
        ServiceSource park = new ServiceSource(new Vector2Int(14, 14), new Vector2Int(2, 2), 4, 0);
        GridData gridA = new GridData(24, 24);
        SeededCity.Seed(gridA);
        SimulationSystem a = new SimulationSystem(gridA, new RoadNetwork(gridA), m_Config);
        a.Sources = new[] { plant, park };
        Run(a, 40);

        SimulationSystem b = LoadIntoNew(SaveSystem.Capture(gridA, a), out GridData gridB, new[] { park, plant });

        for (int y = 0; y < 24; y++)
        {
            for (int x = 0; x < 24; x++)
            {
                Vector2Int cell = new Vector2Int(x, y);
                Assert.AreEqual(a.Power.IsPowered(cell), b.Power.IsPowered(cell), $"powered {cell}");
            }
        }
        Assert.AreEqual(a.Population.Happiness.Services, b.Population.Happiness.Services, 1e-5f);

        Run(a, 30);
        Run(b, 30);

        Assert.AreEqual(a.Population.Population, b.Population.Population);
        Assert.AreEqual(a.Population.AverageHappiness, b.Population.AverageHappiness);
        AssertSameGrid(gridA, gridB);
    }

    [Test]
    public void CreateNew_UsesStartingValuesOnEmptyMap()
    {
        GridData grid = new GridData(24, 24);
        SeededCity.Seed(grid);
        SimulationSystem sim = new SimulationSystem(grid, new RoadNetwork(grid), m_Config);
        Run(sim, 20);

        SaveData blank = SaveSystem.CreateNew(24, 24, m_Config);
        SaveSystem.ApplyGrid(blank, grid);
        SaveSystem.ApplySimulation(blank, sim);

        Assert.AreEqual(0, grid.CountRoads());
        Assert.AreEqual(m_Config.StartingMoney, sim.Economy.Money);
        Assert.AreEqual(0, sim.Population.Population);
        Assert.AreEqual(0, sim.Population.Housing);
        Assert.AreEqual(m_Config.StartingHappiness, sim.Population.AverageHappiness);
        Assert.AreEqual(1, blank.Day);
    }

    [Test]
    public void ApplyGrid_ResizesMapToSaveSize()
    {
        GridData grid = new GridData(24, 24);
        int resized = 0;
        grid.OnResized += () => resized++;

        SaveSystem.ApplyGrid(SaveSystem.CreateNew(10, 12, m_Config), grid);
        SaveSystem.ApplyGrid(SaveSystem.CreateNew(10, 12, m_Config), grid);   // same size: no resize

        Assert.AreEqual(10, grid.Width);
        Assert.AreEqual(12, grid.Height);
        Assert.AreEqual(1, resized);
    }

    [Test]
    public void LoadIntoDifferentSizedMap_ThenContinue_MatchesUninterruptedRun()
    {
        // The running game keeps one GridData and its systems for the whole session, so a load into a
        // map of another size must leave roads, power and coverage exactly as on a fresh map.
        ServiceSource plant = new ServiceSource(new Vector2Int(0, 9), new Vector2Int(3, 3), 0, 400);
        ServiceSource park = new ServiceSource(new Vector2Int(14, 14), new Vector2Int(2, 2), 4, 0);
        GridData gridA = new GridData(24, 24);
        SeededCity.Seed(gridA);
        SimulationSystem a = new SimulationSystem(gridA, new RoadNetwork(gridA), m_Config);
        a.Sources = new[] { plant, park };
        Run(a, 40);
        SaveData saved = SaveSystem.Capture(gridA, a);

        // A bigger city with its own buildings, running before the load.
        GridData gridB = new GridData(40, 40);
        SeededCity.Seed(gridB);
        SimulationSystem b = new SimulationSystem(gridB, new RoadNetwork(gridB), m_Config);
        b.Sources = new[] { new ServiceSource(new Vector2Int(0, 17), new Vector2Int(3, 3), 0, 600) };
        Run(b, 25);

        b.Sources = null;                       // ClearAllBuildings
        SaveSystem.ApplyGrid(saved, gridB);
        b.Sources = new[] { park, plant };      // buildings re-placed
        SaveSystem.ApplySimulation(saved, b);

        Assert.AreEqual(24, gridB.Width);
        Assert.AreEqual(a.Power.Supply, b.Power.Supply);
        Assert.AreEqual(a.Power.Load, b.Power.Load);
        Assert.AreEqual(a.Population.Happiness.Services, b.Population.Happiness.Services, 1e-5f);

        Run(a, 30);
        Run(b, 30);

        Assert.AreEqual(a.Population.Population, b.Population.Population);
        Assert.AreEqual(a.Economy.Money, b.Economy.Money, 1e-3f);
        AssertSameGrid(gridA, gridB);
    }

    // --- JSON / file ---

    [Test]
    public void Json_KeepsCalendarAndBuildings()
    {
        SaveData data = SaveSystem.CreateNew(4, 4, m_Config);
        data.Day = 12;
        data.Month = 3;
        data.Year = 2;
        data.Speed = (int)GameSpeed.x4;
        data.Buildings.Add(new BuildingRecord("park", 1, 2, 3));

        Assert.IsTrue(SaveSystem.TryFromJson(SaveSystem.ToJson(data), out SaveData loaded, out string error), error);

        Assert.AreEqual(12, loaded.Day);
        Assert.AreEqual(3, loaded.Month);
        Assert.AreEqual(2, loaded.Year);
        Assert.AreEqual((int)GameSpeed.x4, loaded.Speed);
        Assert.AreEqual(1, loaded.Buildings.Count);
        Assert.AreEqual("park", loaded.Buildings[0].Id);
        Assert.AreEqual(1, loaded.Buildings[0].X);
        Assert.AreEqual(2, loaded.Buildings[0].Y);
        Assert.AreEqual(3, loaded.Buildings[0].Rotation);
    }

    [Test]
    public void TryFromJson_RejectsBadInput()
    {
        Assert.IsFalse(SaveSystem.TryFromJson("not json", out _, out string corrupt));
        Assert.IsNotNull(corrupt);

        SaveData future = SaveSystem.CreateNew(4, 4, m_Config);
        future.Version = SaveData.CurrentVersion + 1;
        Assert.IsFalse(SaveSystem.TryFromJson(SaveSystem.ToJson(future), out _, out _));

        SaveData truncated = SaveSystem.CreateNew(4, 4, m_Config);
        truncated.Zones = new byte[3];
        Assert.IsFalse(SaveSystem.TryFromJson(SaveSystem.ToJson(truncated), out _, out _));

        SaveData huge = SaveSystem.CreateNew(4, 4, m_Config);
        huge.Width = SaveSystem.MaxMapSize + 1;
        Assert.IsFalse(SaveSystem.TryFromJson(SaveSystem.ToJson(huge), out _, out _));
    }

    [Test]
    public void File_WriteThenRead_RoundTrips_AndMissingFileFails()
    {
        string path = Path.Combine(m_TempDir, "sub", "city.json");
        Assert.IsFalse(SaveSystem.TryRead(path, out _, out _));

        SaveData data = SaveSystem.CreateNew(4, 4, m_Config);
        data.Money = 1234.5f;
        Assert.IsTrue(SaveSystem.TryWrite(path, data, out string writeError), writeError);
        // Overwrite an existing save.
        data.Money = 999f;
        Assert.IsTrue(SaveSystem.TryWrite(path, data, out writeError), writeError);

        Assert.IsTrue(SaveSystem.TryRead(path, out SaveData loaded, out string readError), readError);
        Assert.AreEqual(999f, loaded.Money);
        Assert.IsFalse(File.Exists(path + ".tmp"));
    }
}
