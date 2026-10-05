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

    // --- lane checks (22c) ---

    [Test]
    public void CheckPair_NeedsFreeLandOrLowerRoadOnBothLanes()
    {
        var grid = new GridData(8, 8);
        Assert.AreEqual(RoadLayout.LaneProblem.None, RoadLayout.CheckPair(grid, A, RoadLayout.North, RoadTiers.Avenue));
        Assert.AreEqual(RoadLayout.LaneProblem.OffMap, RoadLayout.CheckPair(grid, new Vector2Int(7, 7), RoadLayout.East, RoadTiers.Avenue));

        grid.SetRoadTier(A + Vector2Int.up, RoadTiers.Paved);
        Assert.AreEqual(RoadLayout.LaneProblem.None, RoadLayout.CheckPair(grid, A, RoadLayout.North, RoadTiers.Avenue), "a street is upgraded");
        grid.SetRoadTier(A + Vector2Int.up, RoadTiers.Highway);
        Assert.AreEqual(RoadLayout.LaneProblem.Higher, RoadLayout.CheckPair(grid, A, RoadLayout.North, RoadTiers.Avenue));

        grid.SetRoadTier(A + Vector2Int.up, 0);
        grid.Occupy(A + Vector2Int.up, Vector2Int.one, 0, 5);
        Assert.AreEqual(RoadLayout.LaneProblem.Blocked, RoadLayout.CheckPair(grid, A, RoadLayout.North, RoadTiers.Avenue), "a building on the second lane");
    }

    [Test]
    public void CheckPair_RefusesALaneAlreadyPairedElsewhere_ButAcceptsTheSamePair()
    {
        Vector2Int north = A + Vector2Int.up, south = A + Vector2Int.down;
        GridData grid = Avenues(A, north, south);
        grid.SetRoadPair(A, RoadLayout.North);
        Assert.AreEqual(RoadLayout.LaneProblem.None, RoadLayout.CheckPair(grid, A, RoadLayout.North, RoadTiers.Avenue), "already this pair");
        Assert.AreEqual(RoadLayout.LaneProblem.Taken, RoadLayout.CheckPair(grid, A, RoadLayout.South, RoadTiers.Avenue), "A belongs to the north lane");
        Assert.AreEqual(RoadLayout.LaneProblem.Taken, RoadLayout.CheckPair(grid, south, RoadLayout.North, RoadTiers.Avenue));
    }

    [Test]
    public void HeadingOf_IsTheDominantAxis()
    {
        Assert.AreEqual(RoadLayout.East, RoadLayout.HeadingOf(A, A + new Vector2Int(2, 1)));
        Assert.AreEqual(RoadLayout.South, RoadLayout.HeadingOf(A, A + new Vector2Int(0, -1)));
        Assert.AreEqual(RoadLayout.North, RoadLayout.HeadingOf(A, A + new Vector2Int(1, 3)));
        Assert.AreEqual(RoadLayout.None, RoadLayout.HeadingOf(A, A));
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

    // --- M22b: directed traffic (legacy tiers: every road has Paved numbers, so only the direction matters) ---

    private static Vector2Int V(int x, int y) => new Vector2Int(x, y);

    private TrafficSystem Traffic(GridData grid) =>
        new TrafficSystem(grid, m_Config, new CapacityModel(m_Config), new RoadTiers(m_Config));

    private static void Row(GridData grid, int y, int x0, int x1, byte tier)
    {
        for (int x = x0; x <= x1; x++) grid.SetRoadTier(V(x, y), tier);
    }

    private static void Highway(GridData grid, int y, int x0, int x1, byte direction)
    {
        for (int x = x0; x <= x1; x++)
        {
            grid.SetRoadTier(V(x, y), RoadTiers.Highway);
            grid.SetRoadDirection(V(x, y), direction);
        }
    }

    private static void Grown(GridData grid, int x, int y, ZoneType zone)
    {
        grid.SetZone(V(x, y), zone);
        grid.SetBuildingLevel(V(x, y), 1);
    }

    [Test]
    public void OneWay_CarriesTheCommuteOnlyTheLegalWay()
    {
        var grid = new GridData(20, 20);
        Highway(grid, 10, 0, 19, RoadLayout.East);
        Grown(grid, 10, 11, ZoneType.Residential);
        Grown(grid, 14, 9, ZoneType.Commercial);        // east of the home: reachable
        Grown(grid, 6, 9, ZoneType.Commercial);         // west of the home: against the flow
        TrafficSystem traffic = Traffic(grid);
        traffic.Update(1f, 1f, 1f);

        Assert.AreEqual(4f, traffic.Load(V(14, 10)), 1e-4f, "the shop ahead is the sink");
        Assert.AreEqual(0f, traffic.Load(V(6, 10)), 1e-4f, "the shop behind is unreachable");
        Assert.AreEqual(0f, traffic.Load(V(9, 10)), 1e-4f);
    }

    [Test]
    public void OneWay_AnInwardEdgeCellIsNoExit()
    {
        var grid = new GridData(20, 20);
        Row(grid, 10, 0, 19, RoadTiers.Paved);
        Grown(grid, 3, 11, ZoneType.Residential);
        TrafficSystem twoWay = Traffic(grid);
        twoWay.Update(1f, 1f, 1f);
        Assert.AreEqual(4f, twoWay.Load(V(0, 10)), 1e-4f, "the west edge is nearer");

        Highway(grid, 10, 0, 19, RoadLayout.East);
        TrafficSystem oneWay = Traffic(grid);
        oneWay.Update(1f, 1f, 1f);
        Assert.AreEqual(0f, oneWay.Load(V(0, 10)), 1e-4f, "the west end points into town: not an exit");
        Assert.AreEqual(4f, oneWay.Load(V(19, 10)), 1e-4f, "the east end points off the map");
    }

    [Test]
    public void OneWay_ASideStreetIsARampOntoIt()
    {
        var grid = new GridData(20, 20);
        Highway(grid, 10, 0, 19, RoadLayout.East);
        for (int y = 8; y <= 9; y++) grid.SetRoadTier(V(10, y), RoadTiers.Paved);
        Grown(grid, 11, 8, ZoneType.Residential);       // beside the street
        Grown(grid, 14, 11, ZoneType.Commercial);       // beside the highway, ahead
        TrafficSystem traffic = Traffic(grid);
        traffic.Update(1f, 1f, 1f);

        Assert.AreEqual(4f, traffic.Load(V(10, 9)), 1e-4f);
        Assert.AreEqual(4f, traffic.Load(V(10, 10)), 1e-4f, "the ramp joins the highway");
        Assert.AreEqual(4f, traffic.Load(V(14, 10)), 1e-4f);
    }

    [Test]
    public void OneWay_AHeadOnCellBlocksTheRoute()
    {
        var grid = new GridData(20, 20);
        Row(grid, 10, 0, 19, RoadTiers.Paved);
        grid.SetRoadTier(V(12, 10), RoadTiers.Highway);
        grid.SetRoadDirection(V(12, 10), RoadLayout.West);     // between the home and its shop, against the trip
        Grown(grid, 10, 11, ZoneType.Residential);
        Grown(grid, 14, 9, ZoneType.Commercial);
        TrafficSystem traffic = Traffic(grid);
        traffic.Update(1f, 1f, 1f);

        Assert.AreEqual(0f, traffic.Load(V(14, 10)), 1e-4f, "the shop cannot be reached");
        Assert.AreEqual(4f, traffic.Load(V(0, 10)), 1e-4f, "so the commute leaves by the west edge");
    }

    [Test]
    public void OneWay_AHomeWithNoRoutePutsNoTripsOnTheRoads()
    {
        var grid = new GridData(20, 20);
        Highway(grid, 10, 0, 9, RoadLayout.East);
        Highway(grid, 10, 10, 19, RoadLayout.West);             // two streams meeting nose to nose, no exit
        Grown(grid, 10, 11, ZoneType.Residential);
        TrafficSystem traffic = Traffic(grid);
        traffic.Update(1f, 1f, 1f);

        Assert.AreEqual(0f, traffic.Trips, 1e-4f);
        Assert.AreEqual(0f, traffic.HomeTrips(V(10, 11)), 1e-4f);
        Assert.AreEqual(0f, traffic.CommuteCongestion(V(10, 11)), 1e-4f);
    }

    [Test]
    public void OneWay_FlowIsDeterministic()
    {
        GridData Build()
        {
            var grid = new GridData(20, 20);
            Highway(grid, 10, 0, 19, RoadLayout.East);
            Row(grid, 5, 0, 19, RoadTiers.Paved);
            for (int y = 6; y <= 9; y++) grid.SetRoadTier(V(8, y), RoadTiers.Paved);
            Grown(grid, 9, 6, ZoneType.Residential);
            Grown(grid, 9, 7, ZoneType.Residential);
            Grown(grid, 15, 11, ZoneType.Commercial);
            return grid;
        }
        GridData gridA = Build(), gridB = Build();
        TrafficSystem a = Traffic(gridA), b = Traffic(gridB);
        a.Update(1f, 1f, 1f);
        b.Update(1f, 1f, 1f);
        for (int y = 0; y < 20; y++)
        {
            for (int x = 0; x < 20; x++) Assert.AreEqual(b.Load(V(x, y)), a.Load(V(x, y)), 1e-5f, $"({x},{y})");
        }
        Assert.Greater(a.Trips, 0f);
    }

    // --- 22d: stretches (Reverse / Make two-way / Make one-way) ---

    private static void OneWayCell(GridData grid, int x, int y, byte direction)
    {
        grid.SetRoadTier(V(x, y), RoadTiers.Highway);
        grid.SetRoadDirection(V(x, y), direction);
    }

    [Test]
    public void Stretch_ReverseTurnsAStraightRunAround_AndOnlyThatRun()
    {
        var grid = new GridData(12, 12);
        for (int x = 2; x <= 6; x++) OneWayCell(grid, x, 5, RoadLayout.East);
        for (int x = 2; x <= 6; x++) OneWayCell(grid, x, 6, RoadLayout.East);       // a parallel carriageway, separate run

        Assert.AreEqual(5, RoadLayout.CollectStretch(grid, V(4, 5)).Count);
        Assert.AreEqual(5, RoadLayout.ReverseStretch(grid, V(4, 5)));
        for (int x = 2; x <= 6; x++)
        {
            Assert.AreEqual(RoadLayout.West, grid.GetRoadDirection(V(x, 5)), $"x {x}");
            Assert.AreEqual(RoadLayout.East, grid.GetRoadDirection(V(x, 6)), "the neighbour run is untouched");
        }
    }

    [Test]
    public void Stretch_ReverseFollowsABend()
    {
        var grid = new GridData(12, 12);
        OneWayCell(grid, 3, 3, RoadLayout.North);
        OneWayCell(grid, 3, 4, RoadLayout.East);
        OneWayCell(grid, 4, 4, RoadLayout.East);

        Assert.AreEqual(3, RoadLayout.ReverseStretch(grid, V(3, 3)));
        Assert.AreEqual(RoadLayout.West, grid.GetRoadDirection(V(4, 4)), "the end now leads back into the bend");
        Assert.AreEqual(RoadLayout.South, grid.GetRoadDirection(V(3, 4)), "the bend leads down");
        Assert.AreEqual(RoadLayout.South, grid.GetRoadDirection(V(3, 3)), "the old start points back the way it came");
        Assert.IsTrue(RoadLayout.CanStep(grid, V(4, 4), V(3, 4)));
        Assert.IsTrue(RoadLayout.CanStep(grid, V(3, 4), V(3, 3)));
    }

    [Test]
    public void Stretch_MakeTwoWayClearsTheRun_AndMakeLineOneWayRebuildsIt()
    {
        var grid = new GridData(12, 12);
        for (int x = 2; x <= 6; x++) OneWayCell(grid, x, 5, RoadLayout.East);

        Assert.AreEqual(5, RoadLayout.MakeStretchTwoWay(grid, V(3, 5)));
        Assert.IsFalse(grid.AnyOneWay());
        Assert.AreEqual(5, RoadLayout.MakeLineOneWay(grid, V(4, 5), RoadLayout.West));
        for (int x = 2; x <= 6; x++) Assert.AreEqual(RoadLayout.West, grid.GetRoadDirection(V(x, 5)));
        Assert.AreEqual(0, RoadLayout.CollectStretch(grid, V(9, 9)).Count, "not a one-way road");
    }

    // --- fixes after the first play-test: the median is a wall, avenues cross in a junction ---

    private static void LayPair(GridData grid, Vector2Int cell, byte side)
    {
        grid.SetRoadTier(cell, RoadTiers.Avenue);
        grid.SetRoadTier(cell + RoadLayout.Offset(side), RoadTiers.Avenue);
        grid.SetRoadPair(cell, side);
    }

    [Test]
    public void Median_BlocksTheStepBetweenTheTwoLanes_ButNotAlongThem()
    {
        var grid = new GridData(12, 12);
        for (int x = 2; x <= 6; x++) LayPair(grid, V(x, 5), RoadLayout.North);
        Assert.IsFalse(RoadLayout.CanStep(grid, V(4, 5), V(4, 6)), "no cutting across the median");
        Assert.IsFalse(RoadLayout.CanStep(grid, V(4, 6), V(4, 5)));
        Assert.IsTrue(RoadLayout.CanStep(grid, V(4, 5), V(5, 5)), "along a lane is fine");
        Assert.IsTrue(RoadLayout.CanStep(grid, V(4, 6), V(3, 6)));
    }

    [Test]
    public void Median_TrafficStaysInItsLane()
    {
        var grid = new GridData(20, 20);
        for (int x = 0; x <= 19; x++) LayPair(grid, V(x, 10), RoadLayout.North);
        Grown(grid, 10, 9, ZoneType.Residential);       // beside the lower lane
        Grown(grid, 14, 12, ZoneType.Commercial);       // beside the upper lane
        TrafficSystem traffic = Traffic(grid);
        traffic.Update(1f, 1f, 1f);

        Assert.AreEqual(0f, traffic.Load(V(14, 11)), 1e-4f, "the job on the far side of the median is not reachable across it");
        Assert.AreEqual(4f, traffic.Trips, 1e-4f, "so the home commutes out of town along its own lane");
        Assert.AreEqual(4f, traffic.Load(V(19, 10)), 1e-4f);
    }

    [Test]
    public void Crossing_TwoAvenuesAtRightAnglesMakeAnOpenJunction()
    {
        var grid = new GridData(16, 16);
        for (int x = 2; x <= 12; x++) LayPair(grid, V(x, 7), RoadLayout.North);     // horizontal: lanes y 7 / 8
        // a vertical pair (second lane to the east) across it at x 6 / 7
        Assert.AreEqual(RoadLayout.LaneProblem.None, RoadLayout.CheckPair(grid, V(6, 7), RoadLayout.East, RoadTiers.Avenue),
            "the first crossing row is accepted, not 'taken'");
        Assert.IsTrue(RoadLayout.IsCrossing(grid, V(6, 7), RoadLayout.East));
        RoadLayout.MakeJunction(grid, V(6, 7), RoadLayout.East);

        foreach (Vector2Int c in new[] { V(6, 7), V(7, 7), V(6, 8), V(7, 8) })
        {
            Assert.AreEqual(RoadTiers.Avenue, grid.GetRoadTier(c));
            Assert.AreEqual(0, grid.GetRoadPair(c), $"{c} is an open junction cell");
        }
        Assert.AreEqual(RoadLayout.North, grid.GetRoadPair(V(5, 7)), "the lanes either side keep their median");
        Assert.AreEqual(RoadLayout.South, grid.GetRoadPair(V(8, 8)));
        Assert.IsTrue(RoadLayout.CanStep(grid, V(6, 7), V(6, 8)), "cars cross inside the junction");
        Assert.IsFalse(RoadLayout.IsCrossing(grid, V(6, 8), RoadLayout.East), "the junction is no longer a pair to cross");
    }

    [Test]
    public void Crossing_NeedsBothLanesOnPairsAtRightAngles()
    {
        var grid = new GridData(16, 16);
        for (int x = 2; x <= 12; x++) LayPair(grid, V(x, 7), RoadLayout.North);
        Assert.IsFalse(RoadLayout.IsCrossing(grid, V(6, 7), RoadLayout.North), "parallel to the existing pair");
        Assert.AreEqual(RoadLayout.LaneProblem.Taken, RoadLayout.CheckPair(grid, V(6, 7), RoadLayout.South, RoadTiers.Avenue));
        Assert.AreEqual(RoadLayout.LaneProblem.Taken, RoadLayout.CheckPair(grid, V(12, 7), RoadLayout.East, RoadTiers.Avenue),
            "one lane on the avenue, the other off it");
    }
}
