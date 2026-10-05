using NUnit.Framework;
using UnityEngine;

// M22a: the per-cell road layout (a Highway's one-way direction, an Avenue's partner lane), its movement rule and save v8.
public sealed class RoadLayoutTests
{
    private BalanceConfig m_Config;

    private static readonly Vector2Int A = new Vector2Int(3, 3);

    [SetUp]
    public void SetUp() => m_Config = ScriptableObject.CreateInstance<BalanceConfig>();

    [TearDown]
    public void TearDown() => Object.DestroyImmediate(m_Config);

    // --- the codes ---

    [Test]
    public void Codes_OppositeRightLeftAndStepsAgree()
    {
        Assert.AreEqual(RoadLayout.South, RoadLayout.Opposite(RoadLayout.North));
        Assert.AreEqual(RoadLayout.West, RoadLayout.Opposite(RoadLayout.East));
        Assert.AreEqual(RoadLayout.None, RoadLayout.Opposite(RoadLayout.None));
        Assert.AreEqual(RoadLayout.East, RoadLayout.RightOf(RoadLayout.North), "facing north, east is on the right");
        Assert.AreEqual(RoadLayout.South, RoadLayout.RightOf(RoadLayout.East));
        Assert.AreEqual(RoadLayout.West, RoadLayout.RightOf(RoadLayout.South));
        Assert.AreEqual(RoadLayout.North, RoadLayout.RightOf(RoadLayout.West));
        Assert.AreEqual(RoadLayout.West, RoadLayout.LeftOf(RoadLayout.North));
        for (byte code = RoadLayout.North; code <= RoadLayout.West; code++)
        {
            Assert.AreEqual(code, RoadLayout.FromStep(RoadLayout.Offset(code)), "step round trip");
            Assert.AreEqual(Vector2Int.zero, RoadLayout.Offset(code) + RoadLayout.Offset(RoadLayout.Opposite(code)));
        }
        Assert.AreEqual(RoadLayout.None, RoadLayout.FromStep(new Vector2Int(1, 1)));
        Assert.IsFalse(RoadLayout.IsCode(5));
    }

    // --- the movement rule ---

    [Test]
    public void CanStep_TwoWayCellsAllowEveryStep()
    {
        Assert.IsTrue(RoadLayout.CanStep(A, 0, A + Vector2Int.right, 0));
        Assert.IsTrue(RoadLayout.CanStep(A + Vector2Int.right, 0, A, 0));
    }

    [Test]
    public void CanStep_OneWayLeavesOnlyAheadAndRefusesHeadOnEntry()
    {
        Vector2Int east = A + Vector2Int.right;
        // A points east, B (east of A) is two-way: A -> B ok, B -> A not (A would be entered from its front).
        Assert.IsTrue(RoadLayout.CanStep(A, RoadLayout.East, east, 0));
        Assert.IsFalse(RoadLayout.CanStep(east, 0, A, RoadLayout.East), "head-on into a one-way cell");
        Assert.IsFalse(RoadLayout.CanStep(A, RoadLayout.East, A + Vector2Int.up, 0), "a one-way cell cannot turn out of its side");
        Assert.IsFalse(RoadLayout.CanStep(A, RoadLayout.East, A + Vector2Int.left, 0), "or back");
    }

    [Test]
    public void CanStep_RampsEnterFromTheBackOrTheSide()
    {
        Vector2Int highway = A;
        // A highway cell pointing north: a street cell beside it (west) may enter it, and it may leave to the north.
        Vector2Int street = A + Vector2Int.left;
        Assert.IsTrue(RoadLayout.CanStep(street, 0, highway, RoadLayout.North), "side ramp on");
        Assert.IsTrue(RoadLayout.CanStep(A + Vector2Int.down, 0, highway, RoadLayout.North), "from behind");
        Assert.IsFalse(RoadLayout.CanStep(A + Vector2Int.up, 0, highway, RoadLayout.North), "from the front");
        Assert.IsFalse(RoadLayout.CanStep(highway, RoadLayout.North, street, 0), "no side exit: it only leaves ahead");
    }

    [Test]
    public void CanStep_GridOverloadNeedsTwoRoadsInBounds()
    {
        var grid = new GridData(8, 8);
        grid.SetRoadTier(A, RoadTiers.Highway);
        grid.SetRoadTier(A + Vector2Int.right, RoadTiers.Highway);
        grid.SetRoadDirection(A, RoadLayout.East);
        Assert.IsTrue(RoadLayout.CanStep(grid, A, A + Vector2Int.right));
        Assert.IsFalse(RoadLayout.CanStep(grid, A + Vector2Int.right, A));
        Assert.IsFalse(RoadLayout.CanStep(grid, A, A + Vector2Int.up), "no road there");
        Assert.IsFalse(RoadLayout.CanStep(grid, A, new Vector2Int(-1, 3)), "out of bounds");
    }

    // --- GridData: highway direction ---

    [Test]
    public void Direction_StickOnlyToHighways_AndClearsWhenTheTierChanges()
    {
        var grid = new GridData(8, 8);
        grid.SetRoadTier(A, RoadTiers.Paved);
        grid.SetRoadDirection(A, RoadLayout.North);
        Assert.AreEqual(0, grid.GetRoadDirection(A), "a street is never one-way");

        grid.SetRoadTier(A, RoadTiers.Highway);
        grid.SetRoadDirection(A, RoadLayout.North);
        Assert.AreEqual(RoadLayout.North, grid.GetRoadDirection(A));
        grid.SetRoadDirection(A, 9);
        Assert.AreEqual(RoadLayout.North, grid.GetRoadDirection(A), "an invalid code is ignored");
        Assert.IsTrue(grid.AnyOneWay());

        grid.SetRoadTier(A, RoadTiers.Avenue);
        Assert.AreEqual(0, grid.GetRoadDirection(A), "leaving the highway tier makes it two-way");
        Assert.IsFalse(grid.AnyOneWay());

        grid.SetRoadTier(A, RoadTiers.Highway);
        grid.SetRoadDirection(A, RoadLayout.West);
        grid.SetRoadTier(A, 0);
        grid.SetRoadTier(A, RoadTiers.Highway);
        Assert.AreEqual(0, grid.GetRoadDirection(A), "a demolished highway forgets its direction");
    }

    [Test]
    public void Direction_RaisesCellChangedOnlyWhenItChanges()
    {
        var grid = new GridData(8, 8);
        grid.SetRoadTier(A, RoadTiers.Highway);
        int changed = 0;
        grid.OnCellChanged += _ => changed++;
        grid.SetRoadDirection(A, RoadLayout.East);
        grid.SetRoadDirection(A, RoadLayout.East);
        Assert.AreEqual(1, changed);
    }

    // --- GridData: avenue pairs ---

    private static GridData Avenues(params Vector2Int[] cells)
    {
        var grid = new GridData(8, 8);
        foreach (Vector2Int cell in cells) grid.SetRoadTier(cell, RoadTiers.Avenue);
        return grid;
    }

    [Test]
    public void Pair_PointsBothLanesAtEachOther_AndUnpairsBoth()
    {
        Vector2Int north = A + Vector2Int.up;
        GridData grid = Avenues(A, north);
        grid.SetRoadPair(A, RoadLayout.North);
        Assert.AreEqual(RoadLayout.North, grid.GetRoadPair(A));
        Assert.AreEqual(RoadLayout.South, grid.GetRoadPair(north));
        Assert.IsTrue(RoadLayout.TryGetPartner(grid, A, out Vector2Int partner));
        Assert.AreEqual(north, partner);

        grid.SetRoadPair(north, RoadLayout.None);
        Assert.AreEqual(0, grid.GetRoadPair(A));
        Assert.AreEqual(0, grid.GetRoadPair(north));
        Assert.IsFalse(RoadLayout.TryGetPartner(grid, A, out _));
    }

    [Test]
    public void Pair_NeedsTwoAvenueCells()
    {
        GridData grid = Avenues(A);
        grid.SetRoadTier(A + Vector2Int.up, RoadTiers.Paved);
        grid.SetRoadPair(A, RoadLayout.North);
        Assert.AreEqual(0, grid.GetRoadPair(A), "the partner is a street");
        grid.SetRoadPair(A, RoadLayout.West);
        Assert.AreEqual(0, grid.GetRoadPair(A), "the partner is empty");
        grid.SetRoadPair(new Vector2Int(0, 0), RoadLayout.East);
        Assert.AreEqual(0, grid.GetRoadPair(new Vector2Int(0, 0)), "not even a road");
    }

    [Test]
    public void Pair_ChangingOneLaneReleasesTheOther()
    {
        Vector2Int east = A + Vector2Int.right;
        GridData grid = Avenues(A, east);
        grid.SetRoadPair(A, RoadLayout.East);

        grid.SetRoadTier(east, RoadTiers.Highway);      // upgraded
        Assert.AreEqual(0, grid.GetRoadPair(A), "the other lane is a single lane again, not a dangling pair");
        Assert.AreEqual(RoadTiers.Avenue, grid.GetRoadTier(A));

        grid.SetRoadTier(east, RoadTiers.Avenue);
        grid.SetRoadPair(A, RoadLayout.East);
        grid.SetRoadTier(A, 0);                          // demolished
        Assert.AreEqual(0, grid.GetRoadPair(east));
    }

    [Test]
    public void Pair_RepairingReplacesTheOldPartner()
    {
        Vector2Int north = A + Vector2Int.up, south = A + Vector2Int.down;
        GridData grid = Avenues(A, north, south);
        grid.SetRoadPair(A, RoadLayout.North);
        grid.SetRoadPair(A, RoadLayout.South);
        Assert.AreEqual(RoadLayout.South, grid.GetRoadPair(A));
        Assert.AreEqual(RoadLayout.North, grid.GetRoadPair(south));
        Assert.AreEqual(0, grid.GetRoadPair(north), "the old partner was released");
    }

    // --- Export / Import / Resize ---

    [Test]
    public void ExportImport_RoundTripsDirectionsAndPairs()
    {
        GridData source = Avenues(A, A + Vector2Int.right);
        source.SetRoadPair(A, RoadLayout.East);
        source.SetRoadTier(new Vector2Int(0, 6), RoadTiers.Highway);
        source.SetRoadDirection(new Vector2Int(0, 6), RoadLayout.East);

        var target = new GridData(8, 8);
        target.Import(source.ExportZones(), source.ExportRoads(), source.ExportLevels(), null, null, null,
            source.ExportRoadDirections(), source.ExportRoadPairs());

        CollectionAssert.AreEqual(source.ExportRoadDirections(), target.ExportRoadDirections());
        CollectionAssert.AreEqual(source.ExportRoadPairs(), target.ExportRoadPairs());
        Assert.AreEqual(RoadLayout.East, target.GetRoadDirection(new Vector2Int(0, 6)));
        Assert.AreEqual(RoadLayout.West, target.GetRoadPair(A + Vector2Int.right));
    }

    [Test]
    public void Import_DropsLayoutThatDoesNotMatchTheRoads()
    {
        var source = new GridData(8, 8);
        source.SetRoadTier(A, RoadTiers.Avenue);
        source.SetRoadTier(A + Vector2Int.up, RoadTiers.Avenue);
        source.SetRoadTier(new Vector2Int(6, 6), RoadTiers.Paved);
        byte[] roads = source.ExportRoads();
        var directions = new byte[64];
        var pairs = new byte[64];
        pairs[3 * 8 + 3] = RoadLayout.North;          // a one-sided pair: the partner does not point back
        directions[6 * 8 + 6] = RoadLayout.East;      // a direction on a street

        var target = new GridData(8, 8);
        target.Import(source.ExportZones(), roads, source.ExportLevels(), null, null, null, directions, pairs);

        Assert.AreEqual(0, target.GetRoadPair(A));
        Assert.AreEqual(0, target.GetRoadDirection(new Vector2Int(6, 6)));
    }

    [Test]
    public void Import_WithoutLayoutArrays_MeansAllTwoWayAndSingleLane()
    {
        GridData source = Avenues(A, A + Vector2Int.right);
        source.SetRoadPair(A, RoadLayout.East);
        var target = new GridData(8, 8);
        target.Import(source.ExportZones(), source.ExportRoads(), source.ExportLevels());
        Assert.AreEqual(0, target.GetRoadPair(A));
        Assert.IsFalse(target.AnyOneWay());
    }

    [Test]
    public void Resize_ReallocatesTheLayoutLayersEmpty()
    {
        GridData grid = Avenues(A, A + Vector2Int.right);
        grid.SetRoadPair(A, RoadLayout.East);
        grid.SetRoadTier(new Vector2Int(0, 6), RoadTiers.Highway);
        grid.SetRoadDirection(new Vector2Int(0, 6), RoadLayout.East);

        grid.Resize(12, 10);
        Assert.AreEqual(120, grid.ExportRoadPairs().Length);
        Assert.AreEqual(120, grid.ExportRoadDirections().Length);
        Assert.IsFalse(grid.AnyOneWay());
        Assert.AreEqual(0, grid.GetRoadPair(A));

        grid.SetRoadTier(new Vector2Int(11, 9), RoadTiers.Highway);
        grid.SetRoadDirection(new Vector2Int(11, 9), RoadLayout.North);
        Assert.AreEqual(RoadLayout.North, grid.GetRoadDirection(new Vector2Int(11, 9)), "the new map takes layout in its far corner");
    }

    // --- save v8 ---

    [Test]
    public void Save_RoundTripsTheRoadLayout()
    {
        var grid = new GridData(24, 24);
        SimulationSystem sim = SeededCity.Run(grid, m_Config, 10);
        var lane = new Vector2Int(2, 2);
        grid.SetRoadTier(lane, RoadTiers.Avenue);
        grid.SetRoadTier(lane + Vector2Int.right, RoadTiers.Avenue);
        grid.SetRoadPair(lane, RoadLayout.East);
        var highway = new Vector2Int(2, 20);
        grid.SetRoadTier(highway, RoadTiers.Highway);
        grid.SetRoadDirection(highway, RoadLayout.South);

        SaveData saved = SaveSystem.Capture(grid, sim);
        Assert.AreEqual(8, saved.Version);
        Assert.IsTrue(SaveSystem.TryFromJson(SaveSystem.ToJson(saved), out SaveData loaded, out string error), error);

        var restored = new GridData(24, 24);
        SaveSystem.ApplyGrid(loaded, restored);
        CollectionAssert.AreEqual(grid.ExportRoadDirections(), restored.ExportRoadDirections());
        CollectionAssert.AreEqual(grid.ExportRoadPairs(), restored.ExportRoadPairs());
        Assert.AreEqual(RoadLayout.South, restored.GetRoadDirection(highway));
        Assert.AreEqual(RoadLayout.East, restored.GetRoadPair(lane));
    }

    [Test]
    public void V7Save_MigratesToV8_WithTwoWayHighwaysAndSingleLaneAvenues()
    {
        var grid = new GridData(24, 24);
        SimulationSystem sim = SeededCity.Run(grid, m_Config, 10);
        SaveData saved = SaveSystem.Capture(grid, sim);
        saved.Version = 7;
        saved.RoadDirections = null;
        saved.RoadPairs = null;

        Assert.IsTrue(SaveSystem.TryFromJson(SaveSystem.ToJson(saved), out SaveData data, out string error), error);

        Assert.AreEqual(SaveData.CurrentVersion, data.Version);
        Assert.AreEqual(24 * 24, data.RoadDirections.Length);
        Assert.AreEqual(24 * 24, data.RoadPairs.Length);
        Assert.IsTrue(System.Array.TrueForAll(data.RoadDirections, d => d == 0));
        Assert.IsTrue(System.Array.TrueForAll(data.RoadPairs, p => p == 0));
        CollectionAssert.AreEqual(saved.Roads, data.Roads, "nothing else changes");
    }

    [Test]
    public void Save_RejectsBadLayoutBytesAndSizes()
    {
        SaveData saved = SaveSystem.CreateNew(4, 4, m_Config);
        saved.RoadDirections[3] = 5;
        Assert.IsFalse(SaveSystem.TryFromJson(SaveSystem.ToJson(saved), out _, out string error1));
        StringAssert.Contains("direction", error1);

        saved = SaveSystem.CreateNew(4, 4, m_Config);
        saved.RoadPairs[3] = 7;
        Assert.IsFalse(SaveSystem.TryFromJson(SaveSystem.ToJson(saved), out _, out string error2));
        StringAssert.Contains("pair", error2);

        saved = SaveSystem.CreateNew(4, 4, m_Config);
        saved.RoadPairs = new byte[3];
        Assert.IsFalse(SaveSystem.TryFromJson(SaveSystem.ToJson(saved), out _, out _), "a layer of the wrong size");
    }
}
