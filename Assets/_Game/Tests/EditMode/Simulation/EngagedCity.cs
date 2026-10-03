using System;
using System.Collections.Generic;
using System.Text;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

// M11g balance harness: the shipped age / tech / building content played by an "engaged" player.
// The city starts as one road from the map edge to the centre and grows block by block: each
// 4x4 block between grid roads (every 5 cells) is opened nearest-first when a zone with demand runs
// out of empty land, paying for its roads, and zoned for that zone (every ninth block in the opening
// order is kept for services).
// Each day, before the tick, the player keeps research going (advances as soon as the checklist
// allows, otherwise the cheapest available tech) and places research buildings, parks and power
// plants as they unlock and are needed, paying for them and keeping a cash cushion.
// M12: homes or shops held at level 2 by low land value get a park in the middle of their block.
// M13: in the well ages a block no well reaches gets one in its middle; in the piped ages water
// towers / pumping stations are built like plants (the biggest unlocked one) whenever water runs short,
// and a block whose middle holds a well gets a fountain instead of a park.
// Building numbers come from the BuildingDatabase asset through SerializedObject, because
// BuildingDefinition lives in Assembly-CSharp, which test assemblies can't reference.
internal sealed class EngagedCity
{
    public const string AgeDatabasePath = "Assets/_Game/Scriptables/Ages/AgeDatabase.asset";
    public const string TechDatabasePath = "Assets/_Game/Scriptables/Techs/TechDatabase.asset";
    private const string BuildingDatabasePath = "Assets/_Game/Scriptables/Buildings/BuildingDatabase.asset";
    private const int RoadSpacing = 5;

    public struct Building
    {
        public string Id;
        public Vector2Int Size;
        public int Cost;
        public float Upkeep;
        public float Research;
        public int Radius;
        public int Supply;
        public float Pollution;
        public int PollutionRadius;
        public int WaterSupply;
        public int WaterRadius;
        public string RequiredTech;
    }

    private readonly BalanceConfig m_Config;
    private readonly Dictionary<string, Building> m_Buildings = new();
    private readonly List<ServiceSource> m_Sources = new();
    private readonly List<Vector2Int> m_FreeSlots = new();      // 2x2 slots in service blocks
    private readonly List<Vector2Int> m_FreeBlocks = new();     // opened service blocks (plants, 2x2 slots)
    private readonly List<(Vector2Int origin, bool service)> m_Unopened = new();   // nearest first
    private readonly List<Vector2Int> m_ZonedCells = new();
    private readonly List<Vector2Int> m_Reserve = new();       // opened on the way to a service block, not zoned yet
    private readonly Dictionary<string, int> m_Placed = new();
    private CityModifiers m_Modifiers;
    private int m_NextOccupant = 1;

    public GridData Grid { get; }
    public SimulationSystem Sim { get; }
    public AgeDatabase Ages { get; }
    public TechDatabase Techs { get; }
    public int Day { get; private set; }
    public int StartAge { get; }
    public readonly List<(int day, int age, int population)> AgeEntries = new();
    public float MinHappiness { get; private set; } = 1f;
    public float MinMoney { get; private set; } = float.MaxValue;

    public EngagedCity(BalanceConfig config, int startAge, int size = 64)
    {
        m_Config = config;
        Ages = AssetDatabase.LoadAssetAtPath<AgeDatabase>(AgeDatabasePath);
        Techs = AssetDatabase.LoadAssetAtPath<TechDatabase>(TechDatabasePath);
        Assert.IsNotNull(Ages, AgeDatabasePath);
        Assert.IsNotNull(Techs, TechDatabasePath);
        LoadBuildings();

        StartAge = startAge;
        Grid = new GridData(size, size);
        Sim = new SimulationSystem(Grid, new RoadNetwork(Grid), config, Ages, Techs);
        SaveData fresh = SaveSystem.CreateNew(size, size, config, Ages, Techs, startAge);
        LayOut();
        SaveSystem.ApplySimulation(fresh, Sim);
        AgeEntries.Add((0, startAge, 0));
    }

    public int Count(string id) => m_Placed.TryGetValue(id, out int n) ? n : 0;

    private void LoadBuildings()
    {
        var database = AssetDatabase.LoadAssetAtPath<ScriptableObject>(BuildingDatabasePath);
        SerializedProperty entries = new SerializedObject(database).FindProperty("m_Entries");
        for (int i = 0; i < entries.arraySize; i++)
        {
            var so = new SerializedObject(entries.GetArrayElementAtIndex(i).objectReferenceValue);
            var b = new Building
            {
                Id = so.FindProperty("m_Id").stringValue,
                Size = so.FindProperty("m_Size").vector2IntValue,
                Cost = so.FindProperty("m_Cost").intValue,
                Upkeep = so.FindProperty("m_UpkeepPerDay").floatValue,
                Research = so.FindProperty("m_ResearchPerDay").floatValue,
                Radius = so.FindProperty("m_CoverageRadius").intValue,
                Supply = so.FindProperty("m_PowerSupply").intValue,
                Pollution = so.FindProperty("m_Pollution").floatValue,
                PollutionRadius = so.FindProperty("m_PollutionRadius").intValue,
                WaterSupply = so.FindProperty("m_WaterSupply").intValue,
                WaterRadius = so.FindProperty("m_WaterRadius").intValue,
                RequiredTech = so.FindProperty("m_RequiredTech").stringValue,
            };
            m_Buildings[b.Id] = b;
        }
    }

    private int CrossLine => Grid.Width / 2 / RoadSpacing * RoadSpacing;

    // The starting road (free): one grid road from the west map edge to the centre; then the blocks
    // to open, nearest the centre first (each one touches an opened block, so all stay connected).
    private void LayOut()
    {
        int line = CrossLine;
        for (int i = 0; i <= line + RoadSpacing; i++) Grid.SetRoad(new Vector2Int(i, line), true);

        int blocks = (Grid.Width - 1) / RoadSpacing;
        Vector2 centre = new Vector2(line, line);
        for (int by = 0; by < blocks; by++)
        {
            for (int bx = 0; bx < blocks; bx++)
            {
                Vector2Int origin = new Vector2Int(bx * RoadSpacing + 1, by * RoadSpacing + 1);
                m_Unopened.Add((origin, (bx * 7 + by * 3) % 9 == 4));
            }
        }
        m_Unopened.Sort((a, b) => Distance(a.origin, centre).CompareTo(Distance(b.origin, centre)));
        OpenBlock(ZoneType.Residential, free: true);
        OpenBlock(ZoneType.Residential, free: true);
        OpenBlock(ZoneType.Commercial, free: true);
        OpenBlock(ZoneType.Industrial, free: true);
    }

    private static float Distance(Vector2Int origin, Vector2 centre)
    {
        return Vector2.Distance(origin + new Vector2(1.5f, 1.5f), centre);
    }

    // Zones a block for `zone`: first one already opened but kept unzoned, else the next block in
    // order (laying its surrounding roads, paid unless free). zone None opens the next block without
    // zoning it (kept in reserve). Service blocks met in order are kept for services. Returns false
    // when out of blocks or money.
    private bool OpenBlock(ZoneType zone, bool free = false)
    {
        if (zone != ZoneType.None && m_Reserve.Count > 0)
        {
            ZoneBlock(m_Reserve[0], zone);
            m_Reserve.RemoveAt(0);
            return true;
        }
        if (m_Unopened.Count == 0) return false;
        (Vector2Int origin, bool service) = m_Unopened[0];

        var roads = new List<Vector2Int>();
        for (int i = -1; i < RoadSpacing; i++)
        {
            foreach (Vector2Int cell in new[]
            {
                origin + new Vector2Int(i, -1), origin + new Vector2Int(i, RoadSpacing - 1),
                origin + new Vector2Int(-1, i), origin + new Vector2Int(RoadSpacing - 1, i),
            })
            {
                if (Grid.InBounds(cell) && !Grid.IsRoad(cell) && !roads.Contains(cell)) roads.Add(cell);
            }
        }
        float cost = roads.Count * m_Config.RoadCost;
        if (!free && Sim.Economy.Money - cost < Cushion) return false;
        if (!free) Sim.Economy.Spend(cost);
        foreach (Vector2Int cell in roads) Grid.SetRoad(cell, true);
        m_Unopened.RemoveAt(0);

        if (service) m_FreeBlocks.Add(origin);
        else if (zone == ZoneType.None) m_Reserve.Add(origin);
        else ZoneBlock(origin, zone);
        return true;
    }

    // Only the 12 road-facing cells: the 2x2 middle of a 4x4 block has no road access.
    private void ZoneBlock(Vector2Int origin, ZoneType zone)
    {
        int last = RoadSpacing - 2;
        for (int y = 0; y <= last; y++)
        {
            for (int x = 0; x <= last; x++)
            {
                if (x != 0 && x != last && y != 0 && y != last) continue;
                Vector2Int cell = origin + new Vector2Int(x, y);
                Grid.SetZone(cell, zone);
                m_ZonedCells.Add(cell);
            }
        }
    }

    private const float Cushion = 2000f;

    private static readonly ZoneType[] s_Zones = { ZoneType.Residential, ZoneType.Commercial, ZoneType.Industrial };

    // Engaged players let blocks grow up before zoning more: a zone with demand gets a new block when
    // none of its cells can grow or upgrade, unless power or water is what's blocking them (plants,
    // towers and wells fix that).
    // One block per day, the most-demanded zone first.
    private void Expand()
    {
        if (SavingForUtilities) return;
        ZoneType needed = NeededZone(onlyWhenFull: true);
        if (needed != ZoneType.None) OpenBlock(needed);
    }

    private ZoneType NeededZone(bool onlyWhenFull)
    {
        var growable = new int[4];
        var powerBlocked = new int[4];
        DemandSnapshot demand = Sim.Demand.Snapshot;
        foreach (Vector2Int cell in m_ZonedCells)
        {
            GrowthBlocker blocker = Sim.Growth.GetBlocker(cell, demand);
            int zone = (int)Grid.GetZone(cell);
            if (blocker == GrowthBlocker.None) growable[zone]++;
            else if (blocker == GrowthBlocker.NoPower || blocker == GrowthBlocker.PowerAtCapacity
                || blocker == GrowthBlocker.NoWater || blocker == GrowthBlocker.WaterAtCapacity) powerBlocked[zone]++;
        }
        ZoneType best = ZoneType.None;
        float bestDemand = m_Config.GrowthDemandThreshold;
        foreach (ZoneType zone in s_Zones)
        {
            if (onlyWhenFull && (growable[(int)zone] > 0 || powerBlocked[(int)zone] > 0)) continue;
            if (demand.Get(zone) <= bestDemand) continue;
            best = zone;
            bestDemand = demand.Get(zone);
        }
        return best;
    }

    public void RunDays(int days)
    {
        for (int i = 0; i < days; i++) RunDay();
    }

    public void RunDay()
    {
        Research();
        Expand();
        Build();
        int age = Sim.Tech.CurrentAge;
        Sim.Tick();
        Day++;
        if (Sim.Tech.CurrentAge != age) AgeEntries.Add((Day, Sim.Tech.CurrentAge, Sim.Population.Population));
        if (Sim.Population.Population >= m_Config.SmallTownGracePopulation) MinHappiness = Mathf.Min(MinHappiness, Sim.Population.AverageHappiness);
        MinMoney = Mathf.Min(MinMoney, Sim.Economy.Money);
    }

    // Advance as soon as allowed; otherwise keep the cheapest available tech (current age first) going.
    private void Research()
    {
        TechSystem tech = Sim.Tech;
        int population = Sim.Population.Population;
        ResearchProject advance = tech.NextAdvance();
        if (advance != null && !tech.IsPlanned(advance) && tech.CanAdvance(population))
        {
            tech.SetActiveAdvance(population);
            return;
        }
        if (tech.Active != null) return;

        TechDefinition best = null;
        foreach (TechDefinition t in Techs.Techs)
        {
            if (!tech.CanResearch(t)) continue;
            if (best == null || Priority(t) < Priority(best)) best = t;
        }
        if (best != null) tech.SetActive(best);
    }

    // Lower is sooner: current-age techs by cost; techs gating a building (power, water, research) first.
    private float Priority(TechDefinition t)
    {
        float cost = t.Cost;
        foreach (Building b in m_Buildings.Values)
        {
            if (b.RequiredTech == t.Id && (b.Supply > 0 || b.WaterSupply > 0 || b.Research > 0f)) cost *= 0.25f;
        }
        return t.Age == Sim.Tech.CurrentAge ? cost : cost * 4f;
    }

    private bool Unlocked(Building b)
    {
        if (string.IsNullOrEmpty(b.RequiredTech)) return true;
        TechDefinition t = Techs.GetById(b.RequiredTech);
        return t != null && Sim.Tech.IsResearched(t);
    }

    // M13: while power or piped water is short of what the city draws, the player saves for the
    // plant / tower and buys nothing else (no parks, no new blocks), or the utility never gets built.
    private bool SavingForUtilities =>
        (Sim.Rules.UpgradesNeedPower && Sim.Power.Supply < Sim.Power.Demand)
        || (Sim.Water.Mode == WaterRule.Piped && Sim.Water.Network.Supply < Sim.Water.Network.Demand);

    private void Build()
    {
        int population = Sim.Population.Population;
        bool saving = SavingForUtilities;
        foreach (Building b in m_Buildings.Values)
        {
            if (b.Id == "house" || !Unlocked(b)) continue;   // House is placed by growth, not here
            if (saving && b.Supply <= 0 && b.WaterSupply <= 0) continue;
            int want = 0;
            if (b.Research > 0f) want = 1 + population / 800;
            else if (b.Radius > 0 && b.WaterRadius == 0) want = 1 + population / 150;   // parks (fountains: RaiseLandValue)
            else if (b.Supply > 0) want = Sim.Rules.UpgradesNeedPower && NeedsPower(b) ? Count(b.Id) + 1 : Count(b.Id);
            else if (b.WaterSupply > 0)
                want = Sim.Water.Mode == WaterRule.Piped && NeedsWater() && IsChosenWaterSource(b) ? Count(b.Id) + 1 : Count(b.Id);
            want = Mathf.Min(want, b.Supply > 0 || b.WaterSupply > 0 ? 12 : 8);
            if (Count(b.Id) < want) TryPlace(b);
        }
        DigWells();
        if (!saving) RaiseLandValue();
    }

    private Vector2Int BlockOf(Vector2Int cell)
    {
        return new Vector2Int((cell.x - 1) / RoadSpacing * RoadSpacing + 1, (cell.y - 1) / RoadSpacing * RoadSpacing + 1);
    }

    private static readonly Vector2Int[] s_MiddleCells = { new Vector2Int(1, 1), new Vector2Int(2, 1), new Vector2Int(1, 2), new Vector2Int(2, 2) };

    // A free cell of the block's 2x2 middle (no road access there, so it's never zoned), or null.
    private Vector2Int? FreeMiddleCell(Vector2Int block)
    {
        foreach (Vector2Int offset in s_MiddleCells)
        {
            Vector2Int cell = block + offset;
            if (Grid.CanPlace(cell, Vector2Int.one, 0) && Grid.GetZone(cell) == ZoneType.None) return cell;
        }
        return null;
    }

    private bool TryFind(Func<Building, bool> match, out Building found)
    {
        foreach (Building b in m_Buildings.Values)
        {
            if (match(b) && Unlocked(b)) { found = b; return true; }
        }
        found = default;
        return false;
    }

    // M13: in the well ages, any zoned cell no well reaches gets a well in its block's middle (up to
    // two a day, paid). Wells are cheap, so this runs as soon as a block is zoned.
    private void DigWells()
    {
        if (Sim.Water.Mode != WaterRule.Coverage) return;
        if (!TryFind(b => b.WaterRadius > 0 && b.Radius == 0, out Building well)) return;
        int dug = 0;
        foreach (Vector2Int cell in m_ZonedCells)
        {
            if (dug >= 2 || Sim.Economy.Money - well.Cost < Cushion) return;
            if (Sim.Water.HasWater(cell)) continue;
            Vector2Int? spot = FreeMiddleCell(BlockOf(cell));
            if (spot == null) continue;
            PlaceAt(well, spot.Value);
            dug++;
        }
    }

    private const int WaterMargin = 75;

    // A thrifty player: the cheapest affordable water source that covers today's shortfall (plus a
    // growth margin), else the biggest affordable one.
    private bool IsChosenWaterSource(Building b)
    {
        WaterNetwork water = Sim.Water.Network;
        int shortfall = water.Demand + WaterMargin - water.Supply;
        Building? cheapestCovering = null, biggest = null;
        foreach (Building other in m_Buildings.Values)
        {
            if (other.WaterSupply <= 0 || !Unlocked(other) || Sim.Economy.Money - other.Cost < Cushion) continue;
            if (other.WaterSupply >= shortfall && (cheapestCovering == null || other.Cost < cheapestCovering.Value.Cost)) cheapestCovering = other;
            if (biggest == null || other.WaterSupply > biggest.Value.WaterSupply) biggest = other;
        }
        Building? chosen = cheapestCovering ?? biggest;
        return chosen != null && chosen.Value.Id == b.Id;
    }

    // A new tower / pump when supply is short of demand (with a growth margin) or anything is dry.
    private bool NeedsWater()
    {
        WaterNetwork water = Sim.Water.Network;
        return water.Supply < water.Demand + WaterMargin || water.UnservedCells > 0;
    }

    // M12: homes or shops held at level 2 by land value get a park in the free 2x2 middle of their
    // block (no road access there, so it's never zoned). M13: when a well took the middle, a fountain
    // goes in a free middle cell instead (until the cell's service bonus is capped). One a day, paid.
    private void RaiseLandValue()
    {
        bool hasPark = TryFind(b => b.Radius > 0 && b.WaterRadius == 0 && b.Size == new Vector2Int(2, 2), out Building park);
        bool hasFountain = TryFind(b => b.Radius > 0 && b.WaterRadius > 0, out Building fountain);
        if (!hasPark && !hasFountain) return;

        foreach (Vector2Int cell in m_ZonedCells)
        {
            if (!Sim.Growth.IsHeldByLandValue(cell)) continue;
            if (Sim.Coverage.GetCoverage(cell) * m_Config.LandValuePerService >= m_Config.LandValueServiceCap) continue;
            Vector2Int middle = BlockOf(cell) + Vector2Int.one;
            if (hasPark && Grid.CanPlace(middle, park.Size, 0) && Grid.GetZone(middle) == ZoneType.None)
            {
                if (Sim.Economy.Money - park.Cost >= Cushion) PlaceAt(park, middle);
                return;
            }
            Vector2Int? spot = hasFountain ? FreeMiddleCell(BlockOf(cell)) : null;
            if (spot == null) continue;
            if (Sim.Economy.Money - fountain.Cost >= Cushion) PlaceAt(fountain, spot.Value);
            return;
        }
    }

    // A new plant when supply is short of demand (with a growth margin) or anything is unpowered.
    private bool NeedsPower(Building plant)
    {
        PowerSystem power = Sim.Power;
        return power.Supply < power.Demand + plant.Supply / 4 || power.UnpoweredCells > 0;
    }

    private void TryPlace(Building b)
    {
        if (Sim.Economy.Money - b.Cost < Cushion) return;

        Vector2Int origin;
        if (b.Size.x <= 2 && b.Size.y <= 2)
        {
            if (m_FreeSlots.Count == 0)
            {
                if (m_FreeBlocks.Count == 0) OpenServiceBlock();
                if (m_FreeBlocks.Count == 0) return;
                Vector2Int block = m_FreeBlocks[0];
                m_FreeBlocks.RemoveAt(0);
                foreach (Vector2Int offset in new[] { new Vector2Int(0, 0), new Vector2Int(2, 0), new Vector2Int(0, 2), new Vector2Int(2, 2) })
                    m_FreeSlots.Add(block + offset);
            }
            origin = m_FreeSlots[0];
            m_FreeSlots.RemoveAt(0);
            if (b.Size == Vector2Int.one) origin = RoadFacingCell(origin);
        }
        else
        {
            if (m_FreeBlocks.Count == 0) OpenServiceBlock();
            if (m_FreeBlocks.Count == 0) return;
            origin = m_FreeBlocks[0];
            m_FreeBlocks.RemoveAt(0);
        }
        PlaceAt(b, origin);
    }

    // A 1x1 building (a water tower) takes the cell of its 2x2 slot that touches a road, so it can
    // feed the network; the slot's other cells stay unused.
    private Vector2Int RoadFacingCell(Vector2Int slot)
    {
        foreach (Vector2Int offset in new[] { Vector2Int.zero, Vector2Int.right, Vector2Int.up, Vector2Int.one })
        {
            Vector2Int cell = slot + offset;
            foreach (Vector2Int n in CellUtils.Neighbors4)
            {
                if (Grid.InBounds(cell + n) && Grid.IsRoad(cell + n)) return cell;
            }
        }
        return slot;
    }

    private void PlaceAt(Building b, Vector2Int origin)
    {
        Assert.IsTrue(Grid.Occupy(origin, b.Size, 0, m_NextOccupant++), $"{b.Id} at {origin}");
        Sim.Economy.Spend(b.Cost);
        m_Modifiers.UpkeepPerDay += b.Upkeep;
        m_Modifiers.ResearchPerDay += b.Research;
        Sim.Modifiers = m_Modifiers;
        if (b.Radius > 0 || b.Supply > 0 || b.Pollution > 0f || b.WaterSupply > 0 || b.WaterRadius > 0)
        {
            m_Sources.Add(new ServiceSource(origin, b.Size, b.Radius, b.Supply, b.Pollution, b.PollutionRadius,
                b.WaterSupply, b.WaterRadius));
            Sim.Sources = m_Sources.ToArray();
        }
        m_Placed[b.Id] = Count(b.Id) + 1;
    }

    // Opens blocks in order until a service block is open; the ones on the way are kept unzoned in
    // reserve and zoned later when a zone runs out of room.
    private void OpenServiceBlock()
    {
        while (m_FreeBlocks.Count == 0)
        {
            if (!OpenBlock(ZoneType.None)) return;
        }
    }

    public string Report()
    {
        var sb = new StringBuilder();
        sb.Append($"start {Ages[StartAge].Id}, day {Day}: pop {Sim.Population.Population}, ${Sim.Economy.Money:N0} (min {MinMoney:N0}), happiness {Sim.Population.AverageHappiness:F2} (min {MinHappiness:F2}), ");
        sb.Append($"RP/day {Sim.Tech.ResearchPerDay:F1}, researched {Sim.Tech.ResearchedCount}, age {Ages[Sim.Tech.CurrentAge].Id}, roads {Grid.CountRoads()}, power {Sim.Power.Load}/{Sim.Power.Supply}, water {Sim.Water.Network.Load}/{Sim.Water.Network.Supply} (dry {Sim.Water.DryCells})\n  ages: ");
        for (int i = 0; i < AgeEntries.Count; i++)
        {
            var e = AgeEntries[i];
            int span = i + 1 < AgeEntries.Count ? AgeEntries[i + 1].day - e.day : Day - e.day;
            sb.Append($"{Ages[e.age].Id} @d{e.day} (pop {e.population}, {span}d)  ");
        }
        sb.Append("\n  built: ");
        foreach (var pair in m_Placed) sb.Append($"{pair.Key}×{pair.Value} ");
        sb.Append("\n  ").Append(Diagnostics());
        return sb.ToString();
    }

    // Demand, capacity and why zoned cells aren't growing, per zone.
    public string Diagnostics()
    {
        var sb = new StringBuilder();
        DemandSnapshot demand = Sim.Demand.Snapshot;
        PopulationSystem p = Sim.Population;
        sb.Append($"demand R{demand.Residential:F2} C{demand.Commercial:F2} I{demand.Industrial:F2}; housing {p.Housing}, jobs C{p.CommercialJobs}/I{p.IndustrialJobs}, workers {p.Workers}, employed {p.Employed}; ");
        foreach (ZoneType zone in s_Zones)
        {
            var levels = new int[4];
            var blockers = new Dictionary<GrowthBlocker, int>();
            foreach (Vector2Int cell in m_ZonedCells)
            {
                if (Grid.GetZone(cell) != zone || Grid.IsOccupied(cell)) continue;
                levels[Grid.GetBuildingLevel(cell)]++;
                GrowthBlocker blocker = Sim.Growth.GetBlocker(cell, demand);
                blockers[blocker] = blockers.TryGetValue(blocker, out int n) ? n + 1 : 1;
            }
            sb.Append($"{zone.ToString()[0]} L0-3 {levels[0]}/{levels[1]}/{levels[2]}/{levels[3]} [");
            foreach (var pair in blockers) sb.Append($"{pair.Key}:{pair.Value} ");
            sb.Append("] ");
        }
        return sb.ToString();
    }
}
