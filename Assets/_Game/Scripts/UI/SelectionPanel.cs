using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Shows what's on the selected cell (placed building, road, grown cell, or zoned land) and why it
// isn't growing. Refreshes immediately on selection; cell and day changes (a growth tick can change
// dozens of cells) are batched into at most one refresh per frame.
public sealed class SelectionPanel : MonoBehaviour
{
    private enum Action { None, Demolish, Unzone, Repair, Clear }

    [SerializeField] private PlacementController m_Placement;
    [SerializeField] private GameManager m_GameManager;
    [SerializeField] private GameObject m_Root;
    [SerializeField] private TMP_Text m_Title;
    [SerializeField] private TMP_Text m_Body;
    [SerializeField] private Button m_ActionButton;
    [SerializeField] private TMP_Text m_ActionLabel;
    [SerializeField] private Button m_CloseButton;
    [Tooltip("Keep historical building toggle (M11); shown for grown cells when the game has ages.")]
    [SerializeField] private Button m_KeepButton;
    [SerializeField] private TMP_Text m_KeepLabel;

    private readonly StringBuilder m_Text = new();
    private Action m_Action;
    private bool m_ShowKeep;
    private enum RoadLayoutMode { None, OneWay, TwoWay }    // what the road buttons offer for the selected highway (M22)
    private RoadLayoutMode m_RoadLayout;
    private Button m_TwoWayButton;        // runtime copy of the Keep button: "Make two-way" on a one-way highway
    private TMP_Text m_TwoWayLabel;
    private float m_RepairCost;       // of the selected broken plant / tower / pump (M17)
    private bool m_Dirty;

    private void Start()
    {
        if (m_Placement == null || m_GameManager == null) return;

        m_Placement.SelectionChanged += Refresh;
        GameEvents.CellChanged += OnCellChanged;
        GameEvents.DateChanged += OnDateChanged;
        if (m_ActionButton != null) m_ActionButton.onClick.AddListener(OnAction);
        if (m_KeepButton != null)
        {
            m_TwoWayButton = Instantiate(m_KeepButton, m_KeepButton.transform.parent);
            m_TwoWayButton.name = "TwoWayButton";
            m_TwoWayButton.transform.SetSiblingIndex(m_KeepButton.transform.GetSiblingIndex() + 1);
            m_TwoWayLabel = m_TwoWayButton.GetComponentInChildren<TMP_Text>();
            m_TwoWayButton.onClick.RemoveAllListeners();
            m_TwoWayButton.onClick.AddListener(OnTwoWay);
            m_TwoWayButton.gameObject.SetActive(false);
            m_KeepButton.onClick.AddListener(OnKeep);
        }
        if (m_CloseButton != null) m_CloseButton.onClick.AddListener(m_Placement.ClearSelection);
        Refresh();
    }

    private void OnDestroy()
    {
        if (m_Placement != null) m_Placement.SelectionChanged -= Refresh;
        GameEvents.CellChanged -= OnCellChanged;
        GameEvents.DateChanged -= OnDateChanged;
    }

    private void OnCellChanged(Vector2Int cell)
    {
        m_Dirty = true;
    }

    private void OnDateChanged(int day, int month, int year)
    {
        m_Dirty = true;
    }

    private void LateUpdate()
    {
        if (!m_Dirty) return;
        m_Dirty = false;
        if (m_Placement.HasSelection) Refresh();
    }

    private void OnAction()
    {
        if (!m_Placement.HasSelection) return;

        Vector2Int cell = m_Placement.SelectedCell;
        if (m_Action == Action.Demolish || m_Action == Action.Clear) m_Placement.DemolishAt(cell);
        else if (m_Action == Action.Unzone) m_Placement.Unzone(cell);
        else if (m_Action == Action.Repair) RepairSelected(cell);
    }

    // Pays for the repair of the broken source on the selected cell (M17); an insufficient-funds toast otherwise.
    private void RepairSelected(Vector2Int cell)
    {
        BuildingInstance building = m_Placement.GetBuildingAt(cell);
        if (building == null) return;
        SimulationSystem sim = m_GameManager.Simulation;
        float cost = sim.RepairCost(building.Origin);
        if (!sim.Repair(building.Origin))
        {
            GameEvents.RaiseInsufficientFunds(cost);
            return;
        }
        GameEvents.RaiseMoneySpent(cost, building.transform.position);
        GameEvents.RaiseNotification($"{building.Definition.DisplayName} repaired.");
        AudioController.Play(SfxId.Repair, building.transform.position);
        Refresh();
    }

    // Toggles "Keep historical building" on the selected grown cell.
    private void OnKeep()
    {
        if (!m_Placement.HasSelection) return;
        Vector2Int cell = m_Placement.SelectedCell;
        GridData grid = m_GameManager.Grid;
        if (grid.IsRoad(cell))
        {
            // The Keep button is the highway's Reverse / Make one-way button (M22).
            if (m_RoadLayout == RoadLayoutMode.OneWay) RoadLayout.ReverseStretch(grid, cell);
            else if (m_RoadLayout == RoadLayoutMode.TwoWay) RoadLayout.MakeLineOneWay(grid, cell, HighwayAxisHeading(grid, cell));
            Refresh();
            return;
        }
        if (grid.GetBuildingLevel(cell) == 0) return;
        grid.SetHistoric(cell, !grid.IsHistoric(cell));
        Refresh();
    }

    private void OnTwoWay()
    {
        if (!m_Placement.HasSelection) return;
        Vector2Int cell = m_Placement.SelectedCell;
        if (m_RoadLayout == RoadLayoutMode.OneWay) RoadLayout.MakeStretchTwoWay(m_GameManager.Grid, cell);
        Refresh();
    }

    // East when the highway runs along x (a highway neighbour east or west), else north.
    private static byte HighwayAxisHeading(GridData grid, Vector2Int cell)
    {
        foreach (Vector2Int step in new[] { Vector2Int.right, Vector2Int.left })
        {
            Vector2Int n = cell + step;
            if (grid.InBounds(n) && grid.GetRoadTier(n) == GridData.HighwayTier) return RoadLayout.East;
        }
        return RoadLayout.North;
    }

    private static string CodeName(byte code) =>
        code == RoadLayout.North ? "north" : code == RoadLayout.East ? "east" : code == RoadLayout.South ? "south" : "west";

    private void Refresh()
    {
        if (!m_Placement.HasSelection)
        {
            m_Root.SetActive(false);
            return;
        }

        m_Root.SetActive(true);
        m_Text.Clear();
        m_ShowKeep = false;
        m_RoadLayout = RoadLayoutMode.None;
        Vector2Int cell = m_Placement.SelectedCell;
        GridData grid = m_GameManager.Grid;

        BuildingInstance building = m_Placement.GetBuildingAt(cell);
        if (building != null) DescribeBuilding(building);
        else if (grid.IsRoad(cell)) DescribeRoad(cell);
        else if (grid.GetBuildingLevel(cell) > 0) DescribeGrown(cell);
        else if (grid.GetZone(cell) == ZoneType.None && m_GameManager.Simulation.Disasters.IsRubble(cell)) DescribeRubble(cell);
        else DescribeZone(cell);

        m_Body.text = m_Text.ToString().TrimEnd();
        if (m_ActionButton != null) m_ActionButton.gameObject.SetActive(m_Action != Action.None);
        if (m_ActionLabel != null)
        {
            m_ActionLabel.text = m_Action == Action.Unzone ? "Unzone"
                : m_Action == Action.Repair ? $"Repair  ${m_RepairCost:N0}"
                : m_Action == Action.Clear ? "Clear rubble" : "Demolish";
        }
        if (m_KeepButton != null) m_KeepButton.gameObject.SetActive(m_ShowKeep || m_RoadLayout != RoadLayoutMode.None);
        if (m_ShowKeep && m_KeepLabel != null)
        {
            m_KeepLabel.text = m_GameManager.Grid.IsHistoric(m_Placement.SelectedCell) ? "Stop keeping as historic" : "Keep historical building";
        }
        if (m_RoadLayout != RoadLayoutMode.None && m_KeepLabel != null)
        {
            m_KeepLabel.text = m_RoadLayout == RoadLayoutMode.OneWay ? "Reverse direction" : "Make one-way";
        }
        if (m_TwoWayButton != null)
        {
            m_TwoWayButton.gameObject.SetActive(m_RoadLayout == RoadLayoutMode.OneWay);
            if (m_TwoWayLabel != null) m_TwoWayLabel.text = "Make two-way";
        }
    }

    private void DescribeBuilding(BuildingInstance building)
    {
        BuildingDefinition def = building.Definition;
        BalanceConfig balance = m_GameManager.Balance;
        m_Title.text = def.DisplayName;
        Line($"{def.Category}  ·  {def.Size.x}x{def.Size.y}");
        Line($"Upkeep  ${def.UpkeepPerDay:N0} / day");
        if (def.HousingCapacity > 0) Line($"Housing  {def.HousingCapacity}");
        if (def.JobsProvided > 0) Line($"Jobs  {def.JobsProvided}");
        if (def.CoverageRadius > 0)
        {
            Line($"Homes within {def.CoverageRadius} cells: +{balance.ServiceBonusEach:P0} happiness each (up to +{balance.ServiceBonusCap:P0} per home). [V] shows coverage.");
        }
        if (def.PowerSupply > 0) DescribePlant(building);
        if (def.WaterRadius > 0 || def.WaterSupply > 0) DescribeWaterSource(building);
        if (def.Pollution > 0f)
        {
            float points = def.Pollution * m_GameManager.Simulation.TechModifiers.PollutionMultiplier;
            Line($"<color=#B07AA8>Pollutes</color>  {points:0.#} within {def.PollutionRadius} cells — nearby homes lose happiness and land value. [V] Pollution view.");
        }
        if (def.ResearchPerDay > 0f)
        {
            float rp = def.ResearchPerDay * m_GameManager.Simulation.TechModifiers.ResearchMultiplier;
            Line($"Research  +{rp:0.#} RP / day");
        }
        DescribeBuildingHazards(building);
        if (def.CivicKind != ServiceKind.None && def.CivicRadius > 0)
        {
            Line($"{CivicLine(def.CivicKind)} within {def.CivicRadius} cells, strength {def.CivicStrength:P0}.");
            string reached = def.CivicKind == ServiceKind.Fire ? "buildings" : def.CivicKind == ServiceKind.Order ? "homes and shops" : "homes";
            Line($"Reaches {CellsReached(building)} {reached}. [V] Services view.");
            BudgetSystem budget = m_GameManager.Simulation.Budget;
            BudgetLine line = BudgetLineOf(def.CivicKind);
            if (!Mathf.Approximately(budget.GetFunding(line), 1f))
            {
                ServiceSource funded = budget.Fund(new ServiceSource(building.Origin, Vector2Int.one, 0, 0,
                    civicKind: def.CivicKind, civicRadius: def.CivicRadius, civicStrength: def.CivicStrength));
                Line($"<color=#F2C14E>Funding {budget.GetFunding(line):P0}</color>: reach {funded.CivicRadius} (full {def.CivicRadius}), strength {funded.CivicStrength:P0} (full {def.CivicStrength:P0}).");
            }
            BuildingDefinition replacement = m_GameManager.ReplacementFor(def);
            if (replacement != null)
            {
                Line($"<color=#F2C14E>Outdated</color> — replace with the {replacement.DisplayName} (strength {replacement.CivicStrength:P0}, reaches {replacement.CivicRadius} cells).");
            }
        }
        m_Action = m_RepairCost > 0f || m_Repairable ? Action.Repair : Action.Demolish;
    }

    private bool m_Repairable;

    // Fire and breakdown lines of a placed building (M17).
    private void DescribeBuildingHazards(BuildingInstance building)
    {
        DisasterSystem disasters = m_GameManager.Simulation.Disasters;
        m_Repairable = false;
        m_RepairCost = 0f;
        if (disasters.Fire.IsBurning(building.Origin))
        {
            Line($"<color=#FF7A29>On fire</color> — day {disasters.Fire.FireDays(building.Origin)} of {m_GameManager.Balance.FireBurnDays}. Fire cover puts fires out; otherwise it burns down and is not refunded.");
        }
        if (disasters.Breakdowns.IsBroken(building.Origin))
        {
            m_RepairCost = m_GameManager.Simulation.RepairCost(building.Origin);
            m_Repairable = true;
            Line($"<color=#F2665A>Broken down</color> — it supplies nothing for {disasters.Breakdowns.DaysLeft(building.Origin)} more days, or until repaired (${m_RepairCost:N0}).");
        }
    }

    private static BudgetLine BudgetLineOf(ServiceKind kind)
    {
        switch (kind)
        {
            case ServiceKind.Order: return BudgetLine.Order;
            case ServiceKind.Fire: return BudgetLine.Fire;
            case ServiceKind.Health: return BudgetLine.Health;
            default: return BudgetLine.Education;
        }
    }

    private static string CivicLine(ServiceKind kind)
    {
        switch (kind)
        {
            case ServiceKind.Order: return "<color=#5B8DEF>Keeps order</color> (less crime)";
            case ServiceKind.Fire: return "<color=#F2554A>Fights fires</color> (less fire risk)";
            case ServiceKind.Health: return "<color=#59D966>Cares for the sick</color>";
            default: return "<color=#B07AD8>Educates residents</color> (more research)";
        }
    }

    private void DescribePlant(BuildingInstance building)
    {
        BuildingDefinition def = building.Definition;
        PowerSystem power = m_GameManager.Simulation.Power;
        if (!TouchesEnergisedRoad(building))
        {
            Line("<color=#F2665A>Not beside a road</color> — supplies no power. Lay a road along one side.");
            return;
        }

        Line($"Supplies {def.PowerSupply:N0} units along the roads it touches.");
        Line($"City power  {power.Demand:N0} needed / {power.Supply:N0} supplied");
        if (power.UnpoweredCells > 0)
        {
            Line($"<color=#F2665A>{power.UnpoweredCells} building{(power.UnpoweredCells == 1 ? "" : "s")} without power</color> — build another plant or connect their roads.");
        }
    }

    // M13: wells and fountains water a radius in the well ages; towers and pumps feed the roads they
    // touch in the piped ages.
    private void DescribeWaterSource(BuildingInstance building)
    {
        BuildingDefinition def = building.Definition;
        WaterSystem water = m_GameManager.Simulation.Water;
        if (def.WaterRadius > 0)
        {
            if (water.Mode == WaterRule.Coverage)
            {
                Line($"<color=#59A6F2>Waters</color> blocks within {def.WaterRadius} cells — they can grow past level 1. [V] Water view.");
            }
            else
            {
                Line("<color=#9AA3B2>Its water no longer counts — homes now need piped water from towers.</color>");
            }
            if (m_GameManager.IsObsolete(def)) Line($"<color=#F2C14E>Obsolete</color> in the {m_GameManager.ObsoleteAgeName(def)} — can't be built any more.");
        }
        if (def.WaterSupply <= 0) return;
        if (water.Mode != WaterRule.Piped)
        {
            Line("<color=#9AA3B2>Pumps nothing until water is piped (Industrial age).</color>");
            return;
        }
        if (!TouchesCarryingCell(building))
        {
            Line("<color=#F2665A>Not beside a road</color> — supplies no water. Lay a road along one side.");
            return;
        }
        WaterStatus status = water.Status;
        Line($"Pumps {def.WaterSupply:N0} units of water along the roads it touches.");
        Line($"City water  {status.Demand:N0} needed / {status.Supply:N0} supplied");
        if (status.DryCells > 0)
        {
            Line($"<color=#F2665A>{status.DryCells} building{(status.DryCells == 1 ? "" : "s")} without water</color> — build another tower or connect their roads.");
        }
    }

    private bool TouchesCarryingCell(BuildingInstance building)
    {
        foreach (Vector2Int cell in CellUtils.GetFootprint(building.Origin, building.Definition.Size, building.Rotation))
        {
            if (BesideWaterMain(cell)) return true;
        }
        return false;
    }

    private bool BesideWaterMain(Vector2Int cell)
    {
        WaterNetwork network = m_GameManager.Simulation.Water.Network;
        foreach (Vector2Int offset in CellUtils.Neighbors4)
        {
            if (network.IsCarrying(cell + offset)) return true;
        }
        return false;
    }

    // M13: pipes are underground, so the panel says when one runs under the cell.
    private void DescribePipe(Vector2Int cell)
    {
        if (!m_GameManager.Grid.IsPipe(cell)) return;
        bool carrying = m_GameManager.Simulation.Water.Network.IsCarrying(cell);
        Line(carrying
            ? "<color=#59A6F2>A water pipe</color> runs under it."
            : "<color=#9AA3B2>A water pipe runs under it (no tower feeds it yet).</color>");
    }

    private bool TouchesEnergisedRoad(BuildingInstance building)
    {
        foreach (Vector2Int cell in CellUtils.GetFootprint(building.Origin, building.Definition.Size, building.Rotation))
        {
            if (BesideEnergisedRoad(cell)) return true;
        }
        return false;
    }

    private bool BesideEnergisedRoad(Vector2Int cell)
    {
        PowerSystem power = m_GameManager.Simulation.Power;
        foreach (Vector2Int offset in CellUtils.Neighbors4)
        {
            if (power.IsEnergisedRoad(cell + offset)) return true;
        }
        return false;
    }

    private void DescribeRoad(Vector2Int cell)
    {
        bool connected = m_GameManager.Roads.IsConnectedToEntry(cell);
        RoadTiers tiers = m_GameManager.Simulation.RoadTiers;
        byte tier = m_GameManager.Grid.GetRoadTier(cell);
        m_Title.text = tiers.DisplayName(tier);
        Line(connected
            ? "Connected to the map edge."
            : "<color=#F2665A>Not connected to the map edge</color> — nothing along it can grow.");
        if (m_GameManager.Simulation.Power.IsEnergisedRoad(cell)) Line("<color=#FFD133>Carries power</color> from a plant.");
        if (m_GameManager.Simulation.Water.Mode == WaterRule.Piped && m_GameManager.Simulation.Water.Network.IsCarrying(cell))
        {
            Line("<color=#59A6F2>Carries water</color> from a tower.");
        }
        Line($"Upkeep  ${tiers.UpkeepPerDay(tier):0.##} / day");
        TrafficSystem traffic = m_GameManager.Simulation.Traffic;
        float load = traffic.Load(cell);
        float ratio = traffic.Congestion(cell);
        string trafficColor = ratio > 1f ? "#F2665A" : ratio > m_GameManager.Balance.CongestionFree ? "#F2C14E" : "#73D973";
        Line($"Traffic  <color={trafficColor}>{load:0} / {tiers.Capacity(tier):0} trips a day</color>  ({ratio:P0})");
        if (ratio > 1f) Line("<color=#F2665A>Jammed</color> — upgrade it (drag a better road tier over it) or add a parallel street.");
        if (!tiers.Frontage(tier)) Line("<color=#9AA3B2>No frontage: land beside a highway gets no road access.</color>");
        DescribeRoadLayout(cell, tier);
        m_Action = Action.Demolish;
    }

    // M22: an avenue's second lane, a highway's direction.
    private void DescribeRoadLayout(Vector2Int cell, byte tier)
    {
        GridData grid = m_GameManager.Grid;
        if (tier == GridData.AvenueTier)
        {
            Line(grid.GetRoadPair(cell) != RoadLayout.None
                ? "Two-lane avenue: the other lane is to the " + CodeName(grid.GetRoadPair(cell)) + ". Demolishing one lane removes both."
                : "<color=#9AA3B2>Single-lane avenue (from before avenues were two tiles wide).</color>");
        }
        if (tier != GridData.HighwayTier) return;
        byte direction = grid.GetRoadDirection(cell);
        if (direction == RoadLayout.None)
        {
            m_RoadLayout = RoadLayoutMode.TwoWay;
            Line("Two-way highway.");
            return;
        }
        m_RoadLayout = RoadLayoutMode.OneWay;
        Line($"<color=#8FD1FF>One-way, heading {CodeName(direction)}</color> ({RoadLayout.CollectStretch(grid, cell).Count} cells in this stretch). Side streets join it as ramps.");
    }

    private void DescribeGrown(Vector2Int cell)
    {
        GridData grid = m_GameManager.Grid;
        BalanceConfig balance = m_GameManager.Balance;
        SimulationSystem sim = m_GameManager.Simulation;
        ZoneType zone = grid.GetZone(cell);
        int level = grid.GetBuildingLevel(cell);
        int maxLevel = sim.Growth.MaxLevelFor(cell);
        int builtAge = grid.GetBuiltAge(cell);
        string unit = zone == ZoneType.Residential ? "Homes" : "Jobs";

        m_Title.text = $"{ZoneName(zone)} building";
        Line($"Level {level} / {maxLevel}");
        Line($"{unit}  {sim.Capacity.CapacityOf(grid, cell)}");
        if (sim.Rules.UpgradesNeedPower)
        {
            Line(sim.Power.IsPowered(cell)
                ? "<color=#73D973>Powered</color>"
                : "<color=#F2665A>No power</color>");
        }
        if (sim.Water.Mode == WaterRule.Coverage)
        {
            Line(sim.Water.HasWater(cell) ? "<color=#59A6F2>Water</color> from a well" : "<color=#F2665A>No well nearby</color>");
        }
        else if (sim.Water.Mode == WaterRule.Piped)
        {
            Line(sim.Water.HasWater(cell) ? "<color=#59A6F2>Water</color>" : "<color=#F2665A>No water</color>");
        }
        if (zone == ZoneType.Residential)
        {
            int parks = m_GameManager.Simulation.Coverage.GetCoverage(cell);
            float bonus = Mathf.Min(parks * balance.ServiceBonusEach, balance.ServiceBonusCap);
            Line(parks > 0 ? $"Parks nearby  {parks}  (+{bonus:P0} happiness)" : "No park nearby");
        }
        DescribeEnvironment(cell, zone);
        DescribeCivic(cell, zone);
        DescribeCommute(cell, zone);
        DescribePipe(cell);
        DescribeAge(cell, builtAge);
        DescribeCellHazards(cell);
        if (level < maxLevel)
        {
            Line($"Next level: {unit.ToLowerInvariant()} {sim.Capacity.CapacityAt(grid, cell, level + 1, builtAge)}");
            Line(BlockerText(cell, zone, "Upgrade"));
        }
        else if (sim.Growth.IsOutdated(cell) || level < balance.MaxLevel)
        {
            Line(BlockerText(cell, zone, "Rebuild"));
        }
        else
        {
            Line("Fully grown.");
        }
        Line("<size=85%><color=#9AA3B2>Demolishing leaves the zone, so it will regrow.</color></size>");
        m_Action = Action.Demolish;
    }

    // Fire and plague on a grown block (M17).
    private void DescribeCellHazards(Vector2Int cell)
    {
        DisasterSystem disasters = m_GameManager.Simulation.Disasters;
        if (disasters.Fire.IsBurning(cell))
        {
            Line($"<color=#FF7A29>On fire</color> — day {disasters.Fire.FireDays(cell)} of {m_GameManager.Balance.FireBurnDays}. Fire cover puts fires out; otherwise it burns down and leaves rubble.");
        }
        if (disasters.Epidemic.IsInfected(cell)) Line("<color=#8FD14F>Plague</color> — its residents are sick and dying. Health care and a quarantine slow the spread.");
        else if (disasters.Epidemic.IsRecovered(cell)) Line("<color=#9AA3B2>Recovered from the plague — immune until the outbreak ends.</color>");
    }

    // Bare rubble left by a fire (M17).
    private void DescribeRubble(Vector2Int cell)
    {
        DisasterSystem disasters = m_GameManager.Simulation.Disasters;
        m_Title.text = "Rubble";
        Line($"Left by a fire; it clears by itself in {disasters.Rubble[cell.y * m_GameManager.Grid.Width + cell.x]} days.");
        Line("Build, zone or demolish here to clear it now.");
        m_Action = Action.Clear;
    }

    // Pollution, land value (with what makes it up) and heritage (M12).
    private void DescribeEnvironment(Vector2Int cell, ZoneType zone)
    {
        GridData grid = m_GameManager.Grid;
        BalanceConfig balance = m_GameManager.Balance;
        SimulationSystem sim = m_GameManager.Simulation;

        float emission = sim.Pollution.EmissionOf(cell);
        if (emission > 0f)
        {
            Line($"<color=#B07AA8>Pollutes</color>  {emission:0.#} within {sim.Pollution.RadiusOf(cell)} cells");
        }
        float pollution = sim.Pollution.GetPollution(cell);
        if (pollution > 0.05f)
        {
            string cost = zone == ZoneType.Residential && grid.GetBuildingLevel(cell) > 0
                ? $"  (−{ServiceStats.PollutionPenaltyAt(balance, pollution):P0} happiness)"
                : string.Empty;
            Line($"Pollution here  {pollution:0.0}{cost}");
        }
        else if (zone == ZoneType.Residential)
        {
            Line("No pollution");
        }

        LandValueBreakdown value = sim.LandValue.Explain(cell);
        if (zone == ZoneType.Residential || zone == ZoneType.Commercial)
        {
            bool enough = value.Total >= balance.LandValueForLevel3 - 1e-4f;
            string color = enough ? "#73D973" : "#F2C14E";
            Line($"Land value  <color={color}>{value.Total:P0}</color>  (level 3 needs {balance.LandValueForLevel3:P0})");
        }
        else
        {
            Line($"Land value  {value.Total:P0}  (industry doesn't need it)");
        }
        Line($"<size=85%><color=#9AA3B2>{LandValueParts(value)}</color></size>");

        if (grid.IsHistoric(cell) && grid.GetBuildingLevel(cell) > 0)
        {
            Line($"<color=#E8C15A>Heritage</color> — +{balance.HeritageLandValueEach:P0} land value and +{balance.HeritageHappinessEach:P0} happiness " +
                 $"for homes within {balance.HeritageRadius} cells.");
        }
    }

    private static string LandValueParts(LandValueBreakdown value)
    {
        var parts = new StringBuilder($"base {value.Base:P0}");
        if (value.Services > 0f) parts.Append($", parks +{value.Services:P0}");
        if (value.Heritage > 0f) parts.Append($", heritage +{value.Heritage:P0}");
        if (value.Technology > 0f) parts.Append($", tech +{value.Technology:P0}");
        if (value.Pollution < 0f) parts.Append($", pollution −{-value.Pollution:P0}");
        if (value.Crime < 0f) parts.Append($", crime −{-value.Crime:P0}");
        if (value.Traffic < 0f) parts.Append($", traffic −{-value.Traffic:P0}");
        if (value.Density > 0f) parts.Append($", low density +{value.Density:P0}");
        if (value.Density < 0f) parts.Append($", high density −{-value.Density:P0}");
        return parts.ToString();
    }

    // A home's commute: the worst road on its way to work and what that costs in happiness (M16).
    private void DescribeCommute(Vector2Int cell, ZoneType zone)
    {
        if (zone != ZoneType.Residential) return;
        TrafficSystem traffic = m_GameManager.Simulation.Traffic;
        BalanceConfig balance = m_GameManager.Balance;
        float commute = traffic.CommuteCongestion(cell);
        float penalty = TrafficSystem.TrafficPenaltyAt(balance, commute);
        string color = penalty > 0.0005f ? "#F2665A" : commute > balance.CongestionFree ? "#F2C14E" : "#73D973";
        string cost = penalty > 0.0005f ? $"  (−{penalty:P0} happiness)" : string.Empty;
        if (!traffic.HasRoute(cell) && m_GameManager.Roads.HasRoadAccess(cell))
        {
            Line("Commute  <color=#F2665A>no route</color> to work or out of town: one-way roads block every way. Reverse or open one.");
            return;
        }
        Line($"Commute  <color={color}>{commute:P0}</color> of the busiest road on its way to work{cost}");
    }

    // Crime, fire risk, sickness and schooling at a grown cell, with what each costs a home (M14).
    private void DescribeCivic(Vector2Int cell, ZoneType zone)
    {
        BalanceConfig balance = m_GameManager.Balance;
        CivicSystem civic = m_GameManager.Simulation.Civic;
        CivicBreakdown needs = civic.Explain(cell);
        bool home = zone == ZoneType.Residential;
        if (needs.Ramp <= 0f)
        {
            Line($"<size=85%><color=#9AA3B2>Small town: no crime, fire or sickness worries until {balance.CivicFreePopulation} residents.</color></size>");
            if (home && needs.Education > 0f) Line($"Schooling  {needs.Education:P0}");
            return;
        }
        if (home || zone == ZoneType.Commercial)
        {
            string cost = home && needs.Crime > 0.005f ? $"  (−{ServiceStats.CrimePenaltyAt(balance, needs.Crime):P0} happiness)" : string.Empty;
            Line($"Crime  {CivicNeed(needs.Crime)}  (order {needs.Order:P0}){cost}");
        }
        string fireCost = home && needs.FireRisk > 0.005f ? $"  (−{ServiceStats.FirePenaltyAt(balance, needs.FireRisk):P0} happiness)" : string.Empty;
        Line($"Fire risk  {CivicNeed(needs.FireRisk)}  (fire cover {needs.Fire:P0}){fireCost}");
        if (!home) return;
        string sickCost = needs.Sickness > 0.005f ? $"  (−{ServiceStats.HealthPenaltyAt(balance, needs.Sickness):P0} happiness)" : string.Empty;
        Line($"Health care  {needs.Health:P0}{sickCost}");
        if (needs.Education > 0f) Line($"Schooling  {needs.Education:P0}  (its residents add research)");
    }

    private static string CivicNeed(float value)
    {
        string color = value >= 0.25f ? "#F2665A" : value >= 0.1f ? "#F2C14E" : "#73D973";
        return $"<color={color}>{value:P0}</color>";
    }

    // Grown cells a civic building reaches that its line serves (homes for health and education).
    private int CellsReached(BuildingInstance building)
    {
        BuildingDefinition def = building.Definition;
        GridData grid = m_GameManager.Grid;
        Vector2Int size = CellUtils.EffectiveSize(def.Size, building.Rotation);
        int r = def.CivicRadius;
        int count = 0;
        for (int y = building.Origin.y - r; y < building.Origin.y + size.y + r; y++)
        {
            for (int x = building.Origin.x - r; x < building.Origin.x + size.x + r; x++)
            {
                var cell = new Vector2Int(x, y);
                if (!grid.InBounds(cell) || grid.GetBuildingLevel(cell) == 0) continue;
                ZoneType zone = grid.GetZone(cell);
                bool served = def.CivicKind == ServiceKind.Fire ? zone != ZoneType.None
                    : def.CivicKind == ServiceKind.Order ? zone == ZoneType.Residential || zone == ZoneType.Commercial
                    : zone == ZoneType.Residential;
                if (served) count++;
            }
        }
        return count;
    }

    // Built age, outdated / historic state and the Keep toggle (M11; nothing without age data).
    private void DescribeAge(Vector2Int cell, int builtAge)
    {
        TechSystem tech = m_GameManager.Simulation.Tech;
        if (tech == null) return;

        AgeDatabase ages = tech.Ages;
        string built = ages.IsValidIndex(builtAge) ? ages[builtAge].DisplayName : "an unknown age";
        Line($"Built in the {built}");
        if (m_GameManager.Grid.IsHistoric(cell))
        {
            Line("<color=#E8C15A>Historic (kept)</color> — keeps its style and size and is never rebuilt.");
        }
        else if (m_GameManager.Simulation.Growth.IsOutdated(cell))
        {
            Line($"<color=#F2C14E>Outdated</color> — will be rebuilt in the {tech.CurrentAgeDefinition.DisplayName} style.");
        }
        m_ShowKeep = true;
    }

    private void DescribeZone(Vector2Int cell)
    {
        ZoneType zone = m_GameManager.Grid.GetZone(cell);
        m_Title.text = $"{ZoneName(zone)} zone";
        Line("Undeveloped.");
        Line(BlockerText(cell, zone, "Growth"));
        DescribeEnvironment(cell, zone);
        DescribePipe(cell);
        if (m_GameManager.Simulation.Disasters.IsRubble(cell))
        {
            Line($"<color=#F2665A>Rubble</color> from a fire — it clears in {m_GameManager.Simulation.Disasters.Rubble[cell.y * m_GameManager.Grid.Width + cell.x]} days, or paint the zone again to clear it now.");
        }
        if (m_GameManager.Simulation.Rules.UpgradesNeedPower)
        {
            Line(BesideEnergisedRoad(cell)
                ? "Power available on its road."
                : "<color=#9AA3B2>No powered road yet — it can grow to level 1 but needs power to upgrade.</color>");
        }
        WaterSystem water = m_GameManager.Simulation.Water;
        if (water.Mode == WaterRule.Coverage)
        {
            Line(water.HasWater(cell)
                ? "A well reaches it."
                : "<color=#9AA3B2>No well nearby — it can grow to level 1 but needs water to upgrade.</color>");
        }
        else if (water.Mode == WaterRule.Piped)
        {
            Line(BesideWaterMain(cell)
                ? "Water available on its road."
                : "<color=#9AA3B2>No water on its road yet — it can grow to level 1 but needs water to upgrade.</color>");
        }
        m_Action = Action.Unzone;
    }

    // What would raise this cell's land value, from what is lowering it (M14d: crime → order cover).
    private string LandValueFixes(Vector2Int cell)
    {
        LandValueBreakdown value = m_GameManager.Simulation.LandValue.Explain(cell);
        string fixes = "add parks";
        if (value.Crime < -0.005f) fixes += ", keep order (a watch house or police nearby)";
        if (value.Pollution < -0.005f) fixes += " or move industry away";
        if (value.Traffic < -0.005f) fixes += ", upgrade the jammed road beside it";
        return fixes;
    }

    public string BlockerText(Vector2Int cell, ZoneType zone, string verb)
    {
        DemandSnapshot demand = m_GameManager.Demand.Snapshot;
        switch (m_GameManager.Simulation.Growth.GetBlocker(cell, demand))
        {
            case GrowthBlocker.None:
                return $"<color=#73D973>{verb} ready</color> — happens when today's growth budget reaches it.";
            case GrowthBlocker.NoRoadAccess:
                return BesideHighway(cell)
                    ? $"<color=#F2665A>{verb} blocked:</color> highways give no access — lay a street beside it."
                    : $"<color=#F2665A>{verb} blocked:</color> needs an adjacent road connected to the map edge.";
            case GrowthBlocker.LowDemand:
                return $"<color=#F2C14E>{verb} waiting:</color> {ZoneName(zone).ToLowerInvariant()} demand is {demand.Get(zone):P0} " +
                       $"(needs over {m_GameManager.Balance.GrowthDemandThreshold:P0}).";
            case GrowthBlocker.NoPower:
                return $"<color=#F2665A>{verb} blocked:</color> needs power — connect a power plant to its road.";
            case GrowthBlocker.PowerAtCapacity:
                return $"<color=#F2C14E>{verb} waiting:</color> its power network is at capacity — build another plant.";
            case GrowthBlocker.NoWater:
                return m_GameManager.Simulation.Water.Mode == WaterRule.Coverage
                    ? $"<color=#F2665A>{verb} blocked:</color> needs water — dig a well within reach."
                    : $"<color=#F2665A>{verb} blocked:</color> needs water — connect a water tower to its road.";
            case GrowthBlocker.WaterAtCapacity:
                return $"<color=#F2C14E>{verb} waiting:</color> its water network is at capacity — build another water tower.";
            case GrowthBlocker.Rubble:
                return $"<color=#F2665A>{verb} blocked:</color> rubble from a fire — it clears in a few days.";
            case GrowthBlocker.Occupied:
                return $"<color=#F2665A>{verb} blocked:</color> a placed building occupies this cell.";
            case GrowthBlocker.AgeMaxLevel:
                return "<color=#9AA3B2>Highest level for this age.</color>";
            case GrowthBlocker.Outdated:
                return "<color=#F2C14E>Outdated</color> — will be rebuilt in the current age's style.";
            case GrowthBlocker.KeptHistoric:
                return "<color=#9AA3B2>Historic (kept) — highest level for its age.</color>";
            case GrowthBlocker.LowLandValue:
                return $"<color=#F2C14E>{verb} waiting:</color> land value {m_GameManager.Simulation.LandValue.GetLandValue(cell):P0}, " +
                       $"level 3 needs {m_GameManager.Balance.LandValueForLevel3:P0} — {LandValueFixes(cell)}.";
            default:
                return string.Empty;
        }
    }

    // Whether a road without frontage (a highway) touches the cell.
    private bool BesideHighway(Vector2Int cell)
    {
        GridData grid = m_GameManager.Grid;
        RoadTiers tiers = m_GameManager.Simulation.RoadTiers;
        foreach (Vector2Int offset in CellUtils.Neighbors4)
        {
            Vector2Int neighbor = cell + offset;
            if (grid.InBounds(neighbor) && grid.IsRoad(neighbor) && !tiers.Frontage(grid.GetRoadTier(neighbor))) return true;
        }
        return false;
    }

    private void Line(string text)
    {
        if (!string.IsNullOrEmpty(text)) m_Text.AppendLine(KeyBindings.Fill(text));
    }

    private static string ZoneName(ZoneType zone)
    {
        return zone == ZoneType.None ? "Unzoned" : zone.ToString();
    }
}
