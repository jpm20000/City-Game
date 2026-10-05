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
// M14: outdated civic tiers (a newer building of the same line is buildable) are never built, as in the
// game; each day a line whose happiness term (crime, fire risk, sickness) costs more than CivicTrigger
// gets its best buildable building where uncovered homes pay the most (block middles for buildings up to
// 2x2, a service block otherwise) if that removes enough of the loss, unless a new plant or tower is
// wanted or the building's upkeep can't be carried. Techs leading to a needed line are researched first,
// and research buildings past the first wait for the surplus to carry their upkeep.
// M14d: lines go costliest first; buildings bigger than 2x2 may take a reserve block or open the next
// one; a rich young city may build on its cash (60 days of the deficit) for a line costing 0.03+; such a
// line also makes the player save for it (no new blocks, parks or cheaper civic buildings); an outdated
// building whose zoned cells a newer one covers at least as strongly is demolished.
// M17f: with disasters on (the constructor's switch) the player answers events (the first choice when it costs at most a
// quarter of the cash above the cushion (all of it when the last choice costs happiness), else the last), repairs broken plants / towers / pumps when the cash covers
// it above the cushion, and rebuilds burnt placed buildings on their spot (a newer tier instead when theirs is
// outdated). Fire cover, health care and spare plants come from its normal civic and utility rules.
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
        public ServiceKind CivicKind;
        public int CivicRadius;
        public float CivicStrength;
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
    private readonly List<(Building b, Vector2Int origin)> m_Civic = new();   // placed civic buildings (M14d)
    private CityModifiers m_Modifiers;
    private int m_NextOccupant = 1;
    private readonly Dictionary<int, (Building b, Vector2Int origin)> m_ById = new();   // placed buildings by occupant id (M17f)
    private readonly List<(Building b, Vector2Int origin)> m_Rebuild = new();           // burnt, to be rebuilt in place
    public int FiresLit { get; private set; }
    public int BlocksLost { get; private set; }
    public int BuildingsLost { get; private set; }
    public int Outbreaks { get; private set; }
    public int PlagueDeaths { get; private set; }
    public int Breakdowns { get; private set; }
    public int Repairs { get; private set; }
    public int Rebuilt { get; private set; }
    public int EventsAnswered { get; private set; }

    public GridData Grid { get; }
    public SimulationSystem Sim { get; }
    public AgeDatabase Ages { get; }
    public TechDatabase Techs { get; }
    public int Day { get; private set; }
    public int StartAge { get; }
    public readonly List<(int day, int age, int population)> AgeEntries = new();
    public float MinHappiness { get; private set; } = 1f;
    public float MinMoney { get; private set; } = float.MaxValue;

    public EngagedCity(BalanceConfig config, int startAge, int size = 64, bool disasters = false, ulong seed = 1)
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
        SaveData fresh = SaveSystem.CreateNew(size, size, config, Ages, Techs, startAge, disasters, seed);
        LayOut();
        SaveSystem.ApplySimulation(fresh, Sim);
        AgeEntries.Add((0, startAge, 0));
        Sim.BuildingsDestroyed += OnBuildingsDestroyed;
    }

    public int Count(string id) => m_Placed.TryGetValue(id, out int n) ? n : 0;

    // The placed buildings still standing, as save records (M19c: the showcase city is written from a harness run).
    // The harness always places with rotation 0.
    public List<BuildingRecord> PlacedBuildings()
    {
        var records = new List<BuildingRecord>();
        var occupants = new List<int>(m_ById.Keys);
        occupants.Sort();
        foreach (int occupant in occupants)
        {
            (Building b, Vector2Int origin) = m_ById[occupant];
            if (Grid.GetOccupant(origin) != occupant) continue;
            records.Add(new BuildingRecord(b.Id, origin.x, origin.y, 0));
        }
        return records;
    }

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
                CivicKind = (ServiceKind)so.FindProperty("m_CivicKind").intValue,
                CivicRadius = so.FindProperty("m_CivicRadius").intValue,
                CivicStrength = so.FindProperty("m_CivicStrength").floatValue,
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
        // M16: the starting road is the best street tier of the starting age (Dirt in Medieval, Paved from Industrial).
        var starting = new HashSet<string>();
        foreach (TechDefinition tech in TechSystem.StartingTechs(Ages, Techs, StartAge)) starting.Add(tech.Id);
        byte startTier = RoadTiers.BestStreet(Techs, tech => starting.Contains(tech.Id));
        for (int i = 0; i <= line + RoadSpacing; i++) Grid.SetRoadTier(new Vector2Int(i, line), startTier);

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
        byte tier = Sim.RoadTiers.BestStreetTier;
        float cost = roads.Count * Sim.RoadTiers.Cost(tier);
        if (!free && Sim.Economy.Money - cost < Cushion) return false;
        if (!free) Sim.Economy.Spend(cost);
        foreach (Vector2Int cell in roads) Grid.SetRoadTier(cell, tier);
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
        Density density = DensityFor(zone);
        for (int y = 0; y <= last; y++)
        {
            for (int x = 0; x <= last; x++)
            {
                if (x != 0 && x != last && y != 0 && y != last) continue;
                Vector2Int cell = origin + new Vector2Int(x, y);
                Grid.SetZone(cell, zone);
                if (DensityUse != DensityMode.Off) Grid.SetDensity(cell, density);
                m_ZonedCells.Add(cell);
            }
        }
        m_BlocksZoned++;
    }

    private const float Cushion = 2000f;
    // M24d: there is no goods rule. A first version zoned industry whenever supply fell below 0.9 (then 0.8), but a factory only
    // grows with industrial demand, so the zoned blocks stayed empty and the player paid for roads until it went bankrupt
    // (-10,000 at day 120); at the shipped numbers the engaged city's supply stays at 0.94-1.0 on imports anyway.

    private static readonly ZoneType[] s_Zones = { ZoneType.Residential, ZoneType.Commercial, ZoneType.Industrial };

    // Engaged players let blocks grow up before zoning more: a zone with demand gets a new block when
    // none of its cells can grow or upgrade, unless power or water is what's blocking them (plants,
    // towers and wells fix that).
    // One block per day, the most-demanded zone first.
    private void Expand()
    {
        if (SavingForUtilities || SavingForCivic()) return;
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
        if (Sim.Disasters.Enabled) ManageDisasters();
        Research();
        Expand();
        UpgradeJammedRoads();
        if (UseBudget) ManageBudget();
        Build();
        int age = Sim.Tech.CurrentAge;
        Sim.Tick();
        Day++;
        if (Sim.Disasters.Enabled) CountDisasters();
        if (Sim.Tech.CurrentAge != age)
        {
            AgeEntries.Add((Day, Sim.Tech.CurrentAge, Sim.Population.Population));
            RetuneDensities();
        }
        if (Sim.Population.Population >= m_Config.SmallTownGracePopulation) MinHappiness = Mathf.Min(MinHappiness, Sim.Population.AverageHappiness);
        MinMoney = Mathf.Min(MinMoney, Sim.Economy.Money);
    }

    // --- Disasters (M17f) ---

    private void CountDisasters()
    {
        DisasterSystem d = Sim.Disasters;
        FiresLit += d.Fire.Ignitions;
        BlocksLost += d.Fire.LostBlocks;
        BuildingsLost += d.Fire.LostBuildings;
        if (d.Epidemic.Started) Outbreaks++;
        PlagueDeaths += d.Epidemic.Died;
        if (d.Breakdowns.Broke) Breakdowns++;
    }

    // A burnt building leaves the player's lists at once (the sim has dropped its source and released its cells); it is
    // rebuilt in place by ManageDisasters when it can be.
    private void OnBuildingsDestroyed(IReadOnlyList<int> occupants)
    {
        foreach (int occupant in occupants)
        {
            if (!m_ById.TryGetValue(occupant, out var entry)) continue;
            m_ById.Remove(occupant);
            Building b = entry.b;
            m_Sources.RemoveAll(s => s.Origin == entry.origin);
            m_Civic.RemoveAll(c => c.origin == entry.origin);
            m_Modifiers.UpkeepPerDay -= b.Upkeep;
            m_Modifiers.ResearchPerDay -= b.Research;
            m_Placed[b.Id] = Mathf.Max(0, Count(b.Id) - 1);
            m_Rebuild.Add(entry);
        }
        Sim.Modifiers = m_Modifiers;
    }

    private void ManageDisasters()
    {
        // Events wait for an answer in the real game: the first choice when affordable and cheap, else the last.
        RandomEventSystem events = Sim.Disasters.Events;
        EventDefinition pending = events.Pending;
        if (pending != null)
        {
            float spare = Mathf.Max(0f, Sim.Economy.Money - Cushion);
            // Paying is worth more of the cash when declining costs happiness (belts, bans, smog).
            bool declineHurts = false;
            foreach (TechEffect effect in pending.Choices[pending.Choices.Length - 1].Effects ?? new TechEffect[0])
            {
                if (effect.Type == TechEffectType.HappinessBonus && effect.Value < 0f) declineHurts = true;
            }
            bool first = events.CanChoose(0) && events.PriceOf(0) <= (declineHurts ? 1f : 0.25f) * spare;
            if (events.Choose(first ? 0 : pending.Choices.Length - 1)) EventsAnswered++;
        }

        // Repair broken plants, towers and pumps when the cash covers it above the cushion.
        var broken = new List<BrokenRecord>(Sim.Disasters.Broken);
        foreach (BrokenRecord record in broken)
        {
            var origin = new Vector2Int(record.X, record.Y);
            float cost = Sim.RepairCost(origin);
            if (Sim.Economy.Money - cost >= Cushion && Sim.Repair(origin)) Repairs++;
        }

        // Rebuild burnt buildings on their spot; one a newer tier has replaced is not rebuilt (its slot is free again).
        for (int i = m_Rebuild.Count - 1; i >= 0; i--)
        {
            (Building b, Vector2Int origin) = m_Rebuild[i];
            if (!CanBuild(b))
            {
                (b.Size.x <= 2 && b.Size.y <= 2 ? m_FreeSlots : m_FreeBlocks).Add(origin);
                m_Rebuild.RemoveAt(i);
                continue;
            }
            if (Sim.Economy.Money - b.Cost < Cushion) continue;
            PlaceAt(b, origin);
            m_Rebuild.RemoveAt(i);
            Rebuilt++;
        }
    }

    public string DisastersReport()
    {
        return $"disasters: fires lit {FiresLit}, blocks lost {BlocksLost}, buildings lost {BuildingsLost} (rebuilt {Rebuilt}), outbreaks {Outbreaks} (deaths {PlagueDeaths}), " +
               $"breakdowns {Breakdowns} (repaired {Repairs}), events answered {EventsAnswered}";
    }

    // --- Traffic (M16d) ---
    // Each day the busiest jammed road cells (up to RoadUpgradesPerDay, busiest first, ties row-major) go to the
    // cheapest unlocked street / avenue tier that carries 1.25 x their load, paid for, keeping the cash cushion.
    // Never builds highways (the human's lever, like funding). Reads the flow of the last tick.
    public const int RoadUpgradesPerDay = 6;
    public int RoadsUpgraded { get; private set; }

    private void UpgradeJammedRoads()
    {
        TrafficSystem traffic = Sim.Traffic;
        if (traffic.JammedRoads == 0) return;

        var jammed = new List<(float ratio, Vector2Int cell)>();
        for (int y = 0; y < Grid.Height; y++)
        {
            for (int x = 0; x < Grid.Width; x++)
            {
                var cell = new Vector2Int(x, y);
                if (Grid.IsRoad(cell) && traffic.Congestion(cell) > 1f) jammed.Add((traffic.Congestion(cell), cell));
            }
        }
        jammed.Sort((a, b) =>
        {
            int c = b.ratio.CompareTo(a.ratio);
            if (c != 0) return c;
            c = a.cell.y.CompareTo(b.cell.y);
            return c != 0 ? c : a.cell.x.CompareTo(b.cell.x);
        });

        RoadTiers tiers = Sim.RoadTiers;
        int done = 0;
        foreach ((float ratio, Vector2Int cell) in jammed)
        {
            if (done >= RoadUpgradesPerDay) break;
            byte current = Grid.GetRoadTier(cell);
            float load = traffic.Load(cell);
            byte target = 0;
            for (byte t = (byte)(current + 1); t < RoadTiers.Highway; t++)
            {
                if (!tiers.IsUnlocked(t)) continue;
                if (target == 0) target = t;                      // fall back to the first better tier
                if (tiers.Capacity(t) >= 1.25f * load) { target = t; break; }
            }
            if (target == 0) continue;
            float cost = tiers.UpgradeCost(current, target);
            if (Sim.Economy.Money - cost < Cushion) break;
            Sim.Economy.Spend(cost);
            Grid.SetRoadTier(cell, target);
            RoadsUpgraded++;
            done++;
        }
    }

    // --- Budget (M15e) ---
    // Off by default, so every earlier run stays the regression baseline. On, the player also enacts the
    // ordinances that answer a happiness loss it has (or that cost nothing it cannot carry), keeps funding at
    // 100% (that lever is the human's), and takes one loan when it has been saving for a must-build for a while.
    public bool UseBudget { get; set; }
    public int LoansTaken { get; private set; }

    // --- Density (M23e) ---
    // Off (default): every block is Medium, the regression baseline. Gated: the player paints what the age allows
    // (Low before the Renaissance, Medium after) and repaints Low blocks to Medium when it opens. Mixed: also High
    // homes and shops in alternate blocks once High is unlocked for the zone.
    public enum DensityMode { Off, Gated, Mixed }
    public DensityMode DensityUse { get; set; }
    private int m_BlocksZoned;

    private Density DensityFor(ZoneType zone)
    {
        if (DensityUse == DensityMode.Off) return global::Density.Medium;
        int age = Sim.Tech.CurrentAge;
        if (age < m_Config.MediumDensityMinAge) return global::Density.Low;
        if (DensityUse == DensityMode.Mixed && zone != ZoneType.Industrial && age >= m_Config.HighDensityMinAge(zone) && m_BlocksZoned % 2 == 0) return global::Density.High;
        return global::Density.Medium;
    }

    // A Low block painted before Medium opened is repainted when it does (free, like any zoning).
    private void RetuneDensities()
    {
        if (DensityUse == DensityMode.Off || Sim.Tech.CurrentAge < m_Config.MediumDensityMinAge) return;
        foreach (Vector2Int cell in m_ZonedCells)
        {
            if (Grid.GetDensity(cell) == global::Density.Low) Grid.SetDensity(cell, global::Density.Medium);
        }
    }
    private int m_SavingDays;

    public string BudgetReport()
    {
        var names = new List<string>();
        foreach (OrdinanceDefinition o in Sim.Tech.EnactedOrdinances) names.Add(o.Id);
        return $"budget: loans taken {LoansTaken}, open {Sim.Budget.Loans.Count}, ordinances [{string.Join(", ", names)}], ordinance cost/day {Sim.Ledger().Ordinances:F1}";
    }

    private const float OrdinanceSurplusFactor = 4f;   // the day's surplus must be this many times the ordinance's cost

    private void ManageBudget()
    {
        TechSystem tech = Sim.Tech;
        int population = Sim.Population.Population;
        float surplus = Sim.Economy.IncomePerDay - Sim.Economy.ExpensePerDay;

        // A shrinking surplus repeals what costs money.
        if (surplus < 0f)
        {
            foreach (OrdinanceDefinition enacted in new List<OrdinanceDefinition>(tech.EnactedOrdinances))
            {
                if (enacted.DailyCost(population) > 0f) tech.Repeal(enacted);
            }
        }
        else if (!SavingForUtilities && !SavingForCivic() && Sim.Economy.Money >= Cushion)
        {
            foreach (OrdinanceDefinition ordinance in Techs.Ordinances)
            {
                if (tech.IsEnacted(ordinance) || !tech.IsUnlocked(ordinance) || !WantsOrdinance(ordinance)) continue;
                if (surplus < OrdinanceSurplusFactor * ordinance.DailyCost(population)) continue;
                tech.Enact(ordinance);
                break;   // one a day
            }
        }

        // Loans: a long wait for a must-build takes one loan; it is repaid once the pressure is off.
        m_SavingDays = SavingForUtilities || SavingForCivic() ? m_SavingDays + 1 : 0;
        if (m_SavingDays >= 10 && Sim.Budget.Loans.Count == 0 && Sim.TakeLoan()) LoansTaken++;
        if (m_SavingDays == 0 && Sim.Budget.Loans.Count > 0 && Sim.Economy.Money > Sim.Budget.RemainingPrincipal(0) + 3f * Cushion)
        {
            Sim.RepayLoan(0);
        }
    }

    // What the ordinance is for: a happiness term it can raise, a need it answers or a flat gain.
    private bool WantsOrdinance(OrdinanceDefinition ordinance)
    {
        HappinessBreakdown h = Sim.Population.Happiness;
        foreach (TechEffect effect in ordinance.Effects)
        {
            switch (effect.Type)
            {
                case TechEffectType.HappinessBonus:
                    if (effect.Value > 0f) return true;
                    break;
                case TechEffectType.CivicNeedMultiplier:
                    ServiceKind kind = (ServiceKind)System.Enum.Parse(typeof(ServiceKind), effect.Target);
                    if (LineTerm(kind) <= -CivicTrigger) return true;
                    break;
                case TechEffectType.PollutionMultiplier:
                    if (h.Pollution <= -0.02f) return true;
                    break;
                case TechEffectType.ResearchMultiplier:
                    return true;
            }
        }
        return false;
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

        m_Wanted = WantedTechs();
        TechDefinition best = null;
        foreach (TechDefinition t in Techs.Techs)
        {
            if (!tech.CanResearch(t)) continue;
            if (best == null || Priority(t) < Priority(best)) best = t;
        }
        if (best != null) tech.SetActive(best);
    }

    private HashSet<TechDefinition> m_Wanted = new();

    // Techs gating a building the player wants now (power, water, research, or M14 a civic line whose
    // happiness term costs more than CivicTrigger), plus their unresearched prerequisites.
    private HashSet<TechDefinition> WantedTechs()
    {
        var wanted = new HashSet<TechDefinition>();
        var stack = new Stack<TechDefinition>();
        foreach (Building b in m_Buildings.Values)
        {
            if (string.IsNullOrEmpty(b.RequiredTech)) continue;
            if (!(b.Supply > 0 || b.WaterSupply > 0 || b.Research > 0f || NeedsLine(b.CivicKind))) continue;
            TechDefinition t = Techs.GetById(b.RequiredTech);
            if (t != null && !Sim.Tech.IsResearched(t)) stack.Push(t);
        }
        while (stack.Count > 0)
        {
            TechDefinition t = stack.Pop();
            if (!wanted.Add(t)) continue;
            foreach (TechDefinition p in t.Prerequisites)
            {
                if (p != null && !Sim.Tech.IsResearched(p)) stack.Push(p);
            }
        }
        return wanted;
    }

    // Lower is sooner: current-age techs by cost; wanted techs (WantedTechs) first.
    private float Priority(TechDefinition t)
    {
        float cost = t.Cost;
        if (m_Wanted.Contains(t)) cost *= 0.25f;
        return t.Age == Sim.Tech.CurrentAge ? cost : cost * 4f;
    }

    private bool Unlocked(Building b)
    {
        if (string.IsNullOrEmpty(b.RequiredTech)) return true;
        TechDefinition t = Techs.GetById(b.RequiredTech);
        return t != null && Sim.Tech.IsResearched(t);
    }

    // M14, as GameManager.ReplacementFor: a civic building is outdated once a building of the same
    // line from a later age (its RequiredTech's age) is unlocked.
    private bool Outdated(Building b)
    {
        if (b.CivicKind == ServiceKind.None) return false;
        int age = TierAge(b);
        foreach (Building other in m_Buildings.Values)
        {
            if (other.CivicKind == b.CivicKind && TierAge(other) > age && Unlocked(other)) return true;
        }
        return false;
    }

    private int TierAge(Building b)
    {
        TechDefinition t = string.IsNullOrEmpty(b.RequiredTech) ? null : Techs.GetById(b.RequiredTech);
        return t != null ? t.Age : -1;
    }

    private bool CanBuild(Building b) => Unlocked(b) && !Outdated(b);

    // M13: while power or piped water is short of what the city draws, the player saves for the
    // plant / tower and buys nothing else (no parks, no new blocks), or the utility never gets built.
    private bool SavingForUtilities =>
        (Sim.Rules.UpgradesNeedPower && Sim.Power.Supply < Sim.Power.Demand)
        || (Sim.Water.Mode == WaterRule.Piped && Sim.Water.Network.Supply < Sim.Water.Network.Demand);

    // M14d: a line costing more than CivicSaveTrigger makes the player save for its best building
    // (no new blocks or parks) when the day's surplus can carry its upkeep, as for utilities.
    private const float CivicSaveTrigger = 0.03f;

    private bool SavingForCivic()
    {
        foreach (ServiceKind line in s_NeedLines)
        {
            if (LineTerm(line) > -CivicSaveTrigger) continue;
            if (!TryFind(b => b.CivicKind == line && !Outdated(b), out Building best)) continue;
            if (!CanCarry(best)) continue;
            if (Sim.Economy.Money - best.Cost < Cushion) return true;
        }
        return false;
    }

    private void Build()
    {
        int population = Sim.Population.Population;
        bool saving = SavingForUtilities;
        bool savingCivic = !saving && SavingForCivic();
        foreach (Building b in m_Buildings.Values)
        {
            if (b.Id == "house" || !CanBuild(b)) continue;   // House is placed by growth, not here
            if (saving && b.Supply <= 0 && b.WaterSupply <= 0) continue;
            if (savingCivic && b.Radius > 0 && b.WaterRadius == 0) continue;   // no parks while saving for civic
            int want = 0;
            // M14: after the first research building, another only when the day's surplus carries its upkeep.
            if (b.Research > 0f && ResearchBuildings() > 0 && Sim.Economy.IncomePerDay - Sim.Economy.ExpensePerDay < b.Upkeep) continue;
            if (b.Research > 0f) want = 1 + population / 800;
            else if (b.Radius > 0 && b.WaterRadius == 0) want = 1 + population / 150;   // parks (fountains: RaiseLandValue)
            else if (b.Supply > 0) want = Sim.Rules.UpgradesNeedPower && NeedsPower(b) ? Count(b.Id) + 1 : Count(b.Id);
            else if (b.WaterSupply > 0)
                want = Sim.Water.Mode == WaterRule.Piped && NeedsWater() && IsChosenWaterSource(b) ? Count(b.Id) + 1 : Count(b.Id);
            want = Mathf.Min(want, b.Supply > 0 || b.WaterSupply > 0 ? 12 : 8);
            if (Count(b.Id) < want) TryPlace(b);
        }
        DigWells();
        if (!saving && !savingCivic) RaiseLandValue();
        if (!saving && !UtilityPending()) PlaceCivic();
    }

    // M14: a new plant or tower is wanted (supply nearly used up); civic upkeep waits until it's built.
    private bool UtilityPending()
    {
        if (Sim.Water.Mode == WaterRule.Piped && NeedsWater()) return true;
        if (!Sim.Rules.UpgradesNeedPower) return false;
        foreach (Building b in m_Buildings.Values)
        {
            if (b.Supply > 0 && CanBuild(b) && NeedsPower(b)) return true;
        }
        return false;
    }

    // M14: a line is worth a building once its happiness term costs more than this.
    private const float CivicTrigger = 0.01f;

    private static readonly ServiceKind[] s_NeedLines = { ServiceKind.Order, ServiceKind.Fire, ServiceKind.Health };

    // M14d: the day's surplus covers the upkeep, or the cash left after building covers CarryDays of
    // the daily deficit it leaves (a rich young city can afford a hospital before its taxes can), but
    // only for a line costing more than CivicSaveTrigger.
    private const int CarryDays = 60;

    private bool CanCarry(Building b)
    {
        float deficit = b.Upkeep - (Sim.Economy.IncomePerDay - Sim.Economy.ExpensePerDay);
        if (deficit <= 0f) return true;
        return LineTerm(b.CivicKind) <= -CivicSaveTrigger && Sim.Economy.Money - b.Cost - Cushion >= deficit * CarryDays;
    }

    private int ResearchBuildings()
    {
        int count = 0;
        foreach (Building b in m_Buildings.Values)
        {
            if (b.Research > 0f) count += Count(b.Id);
        }
        return count;
    }

    private bool NeedsLine(ServiceKind line) => LineTerm(line) <= -CivicTrigger;

    // The line's (signed) happiness term; 0 for education and non-civic buildings.
    private float LineTerm(ServiceKind line)
    {
        HappinessBreakdown happiness = Sim.Population.Happiness;
        return line == ServiceKind.Order ? happiness.Crime
            : line == ServiceKind.Fire ? happiness.Fire
            : line == ServiceKind.Health ? happiness.Health
            : 0f;
    }

    private void PlaceCivic()
    {
        // The line costing the most goes first.
        var lines = new List<ServiceKind>(s_NeedLines);
        lines.Sort((a, b) => LineTerm(a).CompareTo(LineTerm(b)));
        foreach (ServiceKind line in lines)
        {
            if (!NeedsLine(line)) continue;
            if (!TryFind(b => b.CivicKind == line && !Outdated(b), out Building best)) continue;
            if (Sim.Economy.Money - best.Cost < Cushion)
            {
                if (LineTerm(line) <= -CivicSaveTrigger && CanCarry(best)) return;   // saving for it: buy nothing cheaper
                continue;
            }
            if (!CanCarry(best)) continue;   // can't carry its upkeep
            PlaceCivic(best);
        }
    }

    // A building is only worth it when it removes at least this much of the city's happiness loss.
    private const float CivicMinGain = 0.002f;

    // Candidate spots: the free middle of every block with homes paying for this line (buildings up to
    // 2x2), or the next service block (bigger ones). The spot whose reach removes the most happiness loss
    // wins: per grown cell, its penalty x the share of it the new cover takes away (cover never stacks).
    private void PlaceCivic(Building b)
    {
        var candidates = new List<Vector2Int>();
        bool middle = b.Size.x <= 2 && b.Size.y <= 2;
        if (middle)
        {
            var blocks = new HashSet<Vector2Int>();
            foreach (Vector2Int cell in m_ZonedCells)
            {
                if (Penalty(b.CivicKind, Sim.Civic.Explain(cell)) <= 0f || !blocks.Add(BlockOf(cell))) continue;
                Vector2Int? spot = MiddleSpot(BlockOf(cell), b.Size);
                if (spot != null) candidates.Add(spot.Value);
            }
        }
        // Bigger buildings take an open service block, a block kept in reserve, or the next block in
        // order (one block of roads; never OpenServiceBlock, which can pay for a dozen blocks of roads
        // to reach the next service block). Service slots stay for parks, plants and research buildings.
        Vector2Int? next = null;
        if (!middle)
        {
            candidates.AddRange(m_FreeBlocks);
            candidates.AddRange(m_Reserve);
            if (m_Unopened.Count > 0) candidates.Add((next = m_Unopened[0].origin).Value);
        }

        Vector2Int best = default;
        float bestGain = CivicMinGain * Mathf.Max(Sim.Population.Housing, 1);
        bool found = false;
        foreach (Vector2Int origin in candidates)
        {
            float gain = Gain(b, origin);
            if (gain <= bestGain) continue;
            best = origin;
            bestGain = gain;
            found = true;
        }
        if (!found) return;
        if (best == next && !OpenBlock(ZoneType.None)) return;   // into m_Reserve (or m_FreeBlocks)
        if (Sim.Economy.Money - b.Cost < Cushion) return;
        if (!middle && !m_FreeBlocks.Remove(best)) m_Reserve.Remove(best);
        PlaceAt(b, best);
        RetireOutdated(b.CivicKind);
    }

    // M14d: an outdated building whose zoned cells within reach a newer one of its line now covers at
    // least as strongly is demolished, saving its upkeep (the player's "replace with X").
    private void RetireOutdated(ServiceKind line)
    {
        for (int i = m_Civic.Count - 1; i >= 0; i--)
        {
            (Building old, Vector2Int origin) = m_Civic[i];
            if (old.CivicKind != line || !Outdated(old) || !Redundant(i)) continue;
            Grid.Release(origin, old.Size, 0);
            m_Modifiers.UpkeepPerDay -= old.Upkeep;
            Sim.Modifiers = m_Modifiers;
            m_Sources.RemoveAll(s => s.Origin == origin && s.CivicKind == line);
            Sim.Sources = m_Sources.ToArray();
            m_Placed[old.Id]--;
            m_Civic.RemoveAt(i);
            Retired++;
        }
    }

    public int Retired { get; private set; }

    private bool Redundant(int index)
    {
        (Building old, Vector2Int origin) = m_Civic[index];
        int r = old.CivicRadius;
        for (int y = origin.y - r; y < origin.y + old.Size.y + r; y++)
        {
            for (int x = origin.x - r; x < origin.x + old.Size.x + r; x++)
            {
                Vector2Int cell = new Vector2Int(x, y);
                if (!Grid.InBounds(cell) || Grid.GetZone(cell) == ZoneType.None) continue;   // only zoned cells matter
                bool covered = false;
                for (int j = 0; j < m_Civic.Count && !covered; j++)
                {
                    (Building other, Vector2Int o) = m_Civic[j];
                    if (j == index || other.CivicKind != old.CivicKind || other.CivicStrength < old.CivicStrength) continue;
                    int rr = other.CivicRadius;
                    covered = x >= o.x - rr && x < o.x + other.Size.x + rr && y >= o.y - rr && y < o.y + other.Size.y + rr;
                }
                if (!covered) return false;
            }
        }
        return true;
    }

    // The happiness loss (x capacity) the building would remove at this origin.
    private float Gain(Building b, Vector2Int origin)
    {
        float scale = b.CivicKind == ServiceKind.Order ? m_Config.CrimePenalty
            : b.CivicKind == ServiceKind.Fire ? m_Config.FirePenalty
            : m_Config.HealthPenalty;
        float gain = 0f;
        int r = b.CivicRadius;
        for (int y = origin.y - r; y < origin.y + b.Size.y + r; y++)
        {
            for (int x = origin.x - r; x < origin.x + b.Size.x + r; x++)
            {
                Vector2Int cell = new Vector2Int(x, y);
                if (!Grid.InBounds(cell) || Grid.GetZone(cell) == ZoneType.None) continue;
                int capacity = Sim.Capacity.CapacityOf(Grid, cell);
                if (capacity == 0 || (b.CivicKind != ServiceKind.Order && Grid.GetZone(cell) != ZoneType.Residential)) continue;
                CivicBreakdown civic = Sim.Civic.Explain(cell);
                float cover = Cover(b.CivicKind, civic);
                if (cover >= b.CivicStrength || cover >= 1f) continue;
                gain += capacity * Penalty(b.CivicKind, civic) * (b.CivicStrength - cover) / (1f - cover) * scale;
            }
        }
        return gain;
    }

    private static float Penalty(ServiceKind kind, CivicBreakdown civic) =>
        kind == ServiceKind.Order ? civic.Crime : kind == ServiceKind.Fire ? civic.FireRisk : civic.Sickness;

    private static float Cover(ServiceKind kind, CivicBreakdown civic) =>
        kind == ServiceKind.Order ? civic.Order : kind == ServiceKind.Fire ? civic.Fire : civic.Health;

    // Where a building up to 2x2 fits in a block's 2x2 middle (never zoned: no road access), or null.
    private Vector2Int? MiddleSpot(Vector2Int block, Vector2Int size)
    {
        foreach (Vector2Int offset in s_MiddleCells)
        {
            Vector2Int origin = block + offset;
            if (origin.x + size.x > block.x + 3 || origin.y + size.y > block.y + 3) continue;
            if (!Grid.CanPlace(origin, size, 0)) continue;
            bool clear = true;
            foreach (Vector2Int cell in Grid.GetFootprint(origin, size, 0)) clear &= Grid.GetZone(cell) == ZoneType.None;
            if (clear) return origin;
        }
        return null;
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
            if (match(b) && CanBuild(b)) { found = b; return true; }
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
        int occupant = m_NextOccupant++;
        Assert.IsTrue(Grid.Occupy(origin, b.Size, 0, occupant), $"{b.Id} at {origin}");
        m_ById[occupant] = (b, origin);
        foreach (Vector2Int cell in Grid.GetFootprint(origin, b.Size, 0)) Sim.Disasters.ClearRubble(cell);
        Sim.Economy.Spend(b.Cost);
        m_Modifiers.UpkeepPerDay += b.Upkeep;
        m_Modifiers.ResearchPerDay += b.Research;
        Sim.Modifiers = m_Modifiers;
        if (b.Radius > 0 || b.Supply > 0 || b.Pollution > 0f || b.WaterSupply > 0 || b.WaterRadius > 0
            || (b.CivicKind != ServiceKind.None && b.CivicRadius > 0))
        {
            m_Sources.Add(new ServiceSource(origin, b.Size, b.Radius, b.Supply, b.Pollution, b.PollutionRadius,
                b.WaterSupply, b.WaterRadius, b.CivicKind, b.CivicRadius, b.CivicStrength));
            Sim.Sources = m_Sources.ToArray();
        }
        m_Placed[b.Id] = Count(b.Id) + 1;
        if (b.CivicKind != ServiceKind.None && b.CivicRadius > 0) m_Civic.Add((b, origin));
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
        HappinessBreakdown h = Sim.Population.Happiness;
        sb.Append($"\n  civic: crime {h.Crime:F3} fire {h.Fire:F3} health {h.Health:F3}, retired {Retired}, income {Sim.Economy.IncomePerDay:F0} - expense {Sim.Economy.ExpensePerDay:F0} (upkeep {m_Modifiers.UpkeepPerDay:F0})");
        sb.Append($"\n  traffic: trips {Sim.Traffic.Trips:F0}, worst road {Sim.Traffic.WorstCongestion:F2}, jammed {Sim.Traffic.JammedRoads}, term {h.Traffic:F3}, roads upgraded {RoadsUpgraded}");
        if (Sim.Disasters.Enabled) sb.Append("\n  ").Append(DisastersReport());
        if (Sim.GoodsActive) sb.Append("\n  ").Append(GoodsLine());
        sb.Append("\n  built: ");
        foreach (var pair in m_Placed) sb.Append($"{pair.Key}×{pair.Value} ");
        sb.Append("\n  ").Append(Diagnostics());
        return sb.ToString();
    }

    public string GoodsLine()
    {
        GoodsReport g = Sim.Goods.Last;
        return $"goods: made {g.Produced:F1} / wanted {g.Demanded:F1}, imported {g.Imported:F1}, exported {g.Exported:F1}, supply {g.Supply:F2}, stock {g.StockAfter:F0}";
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
