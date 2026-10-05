using NUnit.Framework;
using UnityEngine;

// M23a: the per-cell zone density (a zero-filled layer is Medium) and save v9.
public sealed class DensityTests
{
    private BalanceConfig m_Config;

    private static readonly Vector2Int A = new Vector2Int(3, 3);

    [SetUp]
    public void SetUp() => m_Config = ScriptableObject.CreateInstance<BalanceConfig>();

    [TearDown]
    public void TearDown() => Object.DestroyImmediate(m_Config);

    [Test]
    public void ZonedCell_DefaultsToMedium_AndTakesLowAndHigh()
    {
        var grid = new GridData(12, 10);
        grid.SetZone(A, ZoneType.Residential);
        Assert.AreEqual(Density.Medium, grid.GetDensity(A));

        grid.SetDensity(A, Density.High);
        Assert.AreEqual(Density.High, grid.GetDensity(A));
        grid.SetDensity(A, Density.Low);
        Assert.AreEqual(Density.Low, grid.GetDensity(A));
    }

    [Test]
    public void UnzonedCell_IgnoresDensity()
    {
        var grid = new GridData(12, 10);
        grid.SetDensity(A, Density.High);
        Assert.AreEqual(Density.Medium, grid.GetDensity(A));
    }

    [Test]
    public void ChangingOrClearingTheZone_ResetsDensityToMedium()
    {
        var grid = new GridData(12, 10);
        grid.SetZone(A, ZoneType.Commercial);
        grid.SetDensity(A, Density.High);

        grid.SetZone(A, ZoneType.Commercial);
        Assert.AreEqual(Density.High, grid.GetDensity(A), "the same zone again changes nothing");

        grid.SetZone(A, ZoneType.Industrial);
        Assert.AreEqual(Density.Medium, grid.GetDensity(A), "a new zone type starts at Medium");

        grid.SetDensity(A, Density.Low);
        grid.SetZone(A, ZoneType.None);
        Assert.AreEqual(Density.Medium, grid.GetDensity(A));
        grid.SetZone(A, ZoneType.Industrial);
        Assert.AreEqual(Density.Medium, grid.GetDensity(A), "zoning again does not remember the old density");
    }

    [Test]
    public void ChangingDensity_RaisesCellChanged_OnlyWhenItChanges()
    {
        var grid = new GridData(12, 10);
        grid.SetZone(A, ZoneType.Residential);
        int raised = 0;
        grid.OnCellChanged += _ => raised++;

        grid.SetDensity(A, Density.Medium);
        Assert.AreEqual(0, raised);
        grid.SetDensity(A, Density.High);
        Assert.AreEqual(1, raised);
    }

    [Test]
    public void DensityKeepsTheBuildingLevel()
    {
        var grid = new GridData(12, 10);
        grid.SetZone(A, ZoneType.Residential);
        grid.SetBuildingLevel(A, 2);
        grid.SetDensity(A, Density.Low);
        Assert.AreEqual(2, grid.GetBuildingLevel(A));
    }

    [Test]
    public void Resize_ReallocatesDensityEmpty()
    {
        var grid = new GridData(12, 10);
        grid.SetZone(A, ZoneType.Residential);
        grid.SetDensity(A, Density.High);

        grid.Resize(8, 8);
        Assert.AreEqual(64, grid.ExportDensities().Length);
        Assert.AreEqual(Density.Medium, grid.GetDensity(A));

        var corner = new Vector2Int(7, 7);
        grid.SetZone(corner, ZoneType.Industrial);
        grid.SetDensity(corner, Density.Low);
        Assert.AreEqual(Density.Low, grid.GetDensity(corner), "the new map takes density in its far corner");
    }

    [Test]
    public void ExportImport_RoundTrips_AndDropsDensityOnUnzonedCells()
    {
        var source = new GridData(12, 10);
        source.SetZone(A, ZoneType.Residential);
        source.SetDensity(A, Density.High);
        source.SetZone(new Vector2Int(5, 5), ZoneType.Industrial);
        source.SetDensity(new Vector2Int(5, 5), Density.Low);

        var target = new GridData(12, 10);
        target.Import(source.ExportZones(), source.ExportRoads(), source.ExportLevels(), null, null, null, null, null,
            source.ExportDensities());
        CollectionAssert.AreEqual(source.ExportDensities(), target.ExportDensities());

        byte[] densities = source.ExportDensities();
        densities[0] = 2;      // cell (0, 0) is not zoned
        var other = new GridData(12, 10);
        other.Import(source.ExportZones(), source.ExportRoads(), source.ExportLevels(), null, null, null, null, null, densities);
        Assert.AreEqual(Density.Medium, other.GetDensity(Vector2Int.zero));

        densities = source.ExportDensities();
        densities[3 + 3 * 12] = 9;      // not a density
        other.Import(source.ExportZones(), source.ExportRoads(), source.ExportLevels(), null, null, null, null, null, densities);
        Assert.AreEqual(Density.Medium, other.GetDensity(A));
    }

    [Test]
    public void Import_WithoutDensities_IsAllMedium()
    {
        var source = new GridData(12, 10);
        source.SetZone(A, ZoneType.Residential);
        source.SetDensity(A, Density.High);

        source.Import(source.ExportZones(), source.ExportRoads(), source.ExportLevels());
        Assert.AreEqual(Density.Medium, source.GetDensity(A));
    }

    // --- save v9 ---

    [Test]
    public void Save_RoundTripsDensities()
    {
        var grid = new GridData(24, 24);
        SimulationSystem sim = SeededCity.Run(grid, m_Config, 10);
        var low = new Vector2Int(1, 22);
        var high = new Vector2Int(2, 22);
        grid.SetZone(low, ZoneType.Residential);
        grid.SetZone(high, ZoneType.Commercial);
        grid.SetDensity(low, Density.Low);
        grid.SetDensity(high, Density.High);

        SaveData saved = SaveSystem.Capture(grid, sim);
        Assert.AreEqual(SaveData.CurrentVersion, saved.Version);
        Assert.IsTrue(SaveSystem.TryFromJson(SaveSystem.ToJson(saved), out SaveData loaded, out string error), error);

        var restored = new GridData(24, 24);
        SaveSystem.ApplyGrid(loaded, restored);
        CollectionAssert.AreEqual(grid.ExportDensities(), restored.ExportDensities());
        Assert.AreEqual(Density.Low, restored.GetDensity(low));
        Assert.AreEqual(Density.High, restored.GetDensity(high));
    }

    [Test]
    public void V8Save_MigratesToV9_AllMedium()
    {
        var grid = new GridData(24, 24);
        SimulationSystem sim = SeededCity.Run(grid, m_Config, 10);
        SaveData saved = SaveSystem.Capture(grid, sim);
        saved.Version = 8;
        saved.Densities = null;

        Assert.IsTrue(SaveSystem.TryFromJson(SaveSystem.ToJson(saved), out SaveData data, out string error), error);

        Assert.AreEqual(SaveData.CurrentVersion, data.Version);
        Assert.AreEqual(24 * 24, data.Densities.Length);
        Assert.IsTrue(System.Array.TrueForAll(data.Densities, d => d == 0));
        CollectionAssert.AreEqual(saved.Zones, data.Zones, "nothing else changes");
    }

    [Test]
    public void Save_RejectsBadDensityBytesAndSizes()
    {
        SaveData saved = SaveSystem.CreateNew(4, 4, m_Config);
        saved.Zones[3] = (byte)ZoneType.Residential;
        saved.Densities[3] = 3;
        Assert.IsFalse(SaveSystem.TryFromJson(SaveSystem.ToJson(saved), out _, out string error1));
        StringAssert.Contains("density", error1);

        saved = SaveSystem.CreateNew(4, 4, m_Config);
        saved.Densities[3] = 1;      // on an unzoned cell
        Assert.IsFalse(SaveSystem.TryFromJson(SaveSystem.ToJson(saved), out _, out string error2));
        StringAssert.Contains("density", error2);

        saved = SaveSystem.CreateNew(4, 4, m_Config);
        saved.Densities = new byte[3];
        Assert.IsFalse(SaveSystem.TryFromJson(SaveSystem.ToJson(saved), out _, out _), "a layer of the wrong size");
    }
}
