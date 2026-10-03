using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Shows what's on the selected cell (placed building, road, grown cell, or zoned land) and why it
// isn't growing. Refreshes immediately on selection; cell and day changes (a growth tick can change
// dozens of cells) are batched into at most one refresh per frame.
public sealed class SelectionPanel : MonoBehaviour
{
    private enum Action { None, Demolish, Unzone }

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
    private bool m_Dirty;

    private void Start()
    {
        if (m_Placement == null || m_GameManager == null) return;

        m_Placement.SelectionChanged += Refresh;
        GameEvents.CellChanged += OnCellChanged;
        GameEvents.DateChanged += OnDateChanged;
        if (m_ActionButton != null) m_ActionButton.onClick.AddListener(OnAction);
        if (m_KeepButton != null) m_KeepButton.onClick.AddListener(OnKeep);
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
        if (m_Action == Action.Demolish) m_Placement.DemolishAt(cell);
        else if (m_Action == Action.Unzone) m_Placement.Unzone(cell);
    }

    // Toggles "Keep historical building" on the selected grown cell.
    private void OnKeep()
    {
        if (!m_Placement.HasSelection) return;
        Vector2Int cell = m_Placement.SelectedCell;
        GridData grid = m_GameManager.Grid;
        if (grid.GetBuildingLevel(cell) == 0) return;
        grid.SetHistoric(cell, !grid.IsHistoric(cell));
        Refresh();
    }

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
        Vector2Int cell = m_Placement.SelectedCell;
        GridData grid = m_GameManager.Grid;

        BuildingInstance building = m_Placement.GetBuildingAt(cell);
        if (building != null) DescribeBuilding(building);
        else if (grid.IsRoad(cell)) DescribeRoad(cell);
        else if (grid.GetBuildingLevel(cell) > 0) DescribeGrown(cell);
        else DescribeZone(cell);

        m_Body.text = m_Text.ToString().TrimEnd();
        if (m_ActionButton != null) m_ActionButton.gameObject.SetActive(m_Action != Action.None);
        if (m_ActionLabel != null) m_ActionLabel.text = m_Action == Action.Unzone ? "Unzone" : "Demolish";
        if (m_KeepButton != null) m_KeepButton.gameObject.SetActive(m_ShowKeep);
        if (m_ShowKeep && m_KeepLabel != null)
        {
            m_KeepLabel.text = m_GameManager.Grid.IsHistoric(m_Placement.SelectedCell) ? "Stop keeping as historic" : "Keep historical building";
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
        m_Action = Action.Demolish;
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
        m_Title.text = "Road";
        Line(connected
            ? "Connected to the map edge."
            : "<color=#F2665A>Not connected to the map edge</color> — nothing along it can grow.");
        if (m_GameManager.Simulation.Power.IsEnergisedRoad(cell)) Line("<color=#FFD133>Carries power</color> from a plant.");
        if (m_GameManager.Simulation.Water.Mode == WaterRule.Piped && m_GameManager.Simulation.Water.Network.IsCarrying(cell))
        {
            Line("<color=#59A6F2>Carries water</color> from a tower.");
        }
        Line($"Upkeep  ${m_GameManager.Balance.RoadUpkeepPerDay:N0} / day");
        m_Action = Action.Demolish;
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
        DescribeAge(cell, builtAge);
        if (level < maxLevel)
        {
            Line($"Next level: {unit.ToLowerInvariant()} {sim.Capacity.Capacity(level + 1, builtAge)}");
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
        return parts.ToString();
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

    private string BlockerText(Vector2Int cell, ZoneType zone, string verb)
    {
        DemandSnapshot demand = m_GameManager.Demand.Snapshot;
        switch (m_GameManager.Simulation.Growth.GetBlocker(cell, demand))
        {
            case GrowthBlocker.None:
                return $"<color=#73D973>{verb} ready</color> — happens when today's growth budget reaches it.";
            case GrowthBlocker.NoRoadAccess:
                return $"<color=#F2665A>{verb} blocked:</color> needs an adjacent road connected to the map edge.";
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
                       $"level 3 needs {m_GameManager.Balance.LandValueForLevel3:P0} — add parks or move industry away.";
            default:
                return string.Empty;
        }
    }

    private void Line(string text)
    {
        if (!string.IsNullOrEmpty(text)) m_Text.AppendLine(text);
    }

    private static string ZoneName(ZoneType zone)
    {
        return zone == ZoneType.None ? "Unzoned" : zone.ToString();
    }
}
