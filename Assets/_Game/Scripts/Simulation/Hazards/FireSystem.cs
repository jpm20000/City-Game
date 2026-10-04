using System;
using System.Collections.Generic;
using UnityEngine;

// Fires (M17). A burning block or placed building burns for BalanceConfig.FireBurnDays days; fire cover (and
// water) can put it out, otherwise it burns down. Per day: ignition (one fire at most, drawn from the grown
// blocks' fire risk), then every fire that was already burning rolls to go out, tries to set its 4-neighbours
// alight (a one-cell road is jumped at a reduced chance, wider roads stop it) and finally burns a day longer or
// down. A fire lit today starts spreading tomorrow. A grown block that burns down drops to level 0 and leaves
// rubble; a placed building's whole footprint burns as one, is released from the grid and reported in
// DestroyedBuildings (the runtime removes the object, no refund). Draws come from DisasterSystem.Random in a
// fixed order: ignition (a chance, then the pick), then per burning unit its extinguish roll and its spread
// rolls in row-major order. The layers live in DisasterSystem (Fires: 0 or the days burnt, 1..FireBurnDays).
public sealed class FireSystem
{
    private static readonly int[] s_Dx = { 1, -1, 0, 0 };   // CellUtils.Neighbors4 order: +x -x +y -y
    private static readonly int[] s_Dy = { 0, 0, 1, -1 };

    private readonly DisasterSystem m_Owner;
    private readonly GridData m_Grid;
    private readonly BalanceConfig m_Config;
    private readonly CivicSystem m_Civic;
    private readonly WaterSystem m_Water;
    private readonly Func<TechModifiers> m_Tech;
    private readonly Func<int> m_Age;
    private readonly SimRandom m_Random;

    private readonly List<int> m_Snapshot = new();
    private readonly List<int> m_Single = new();
    private readonly HashSet<int> m_Processed = new();
    private readonly List<int> m_Destroyed = new();
    private readonly HashSet<int> m_DestroyedCells = new();
    private Dictionary<int, List<int>> m_OccupantCells;

    // This step's results (reset at the start of each Step).
    public int Ignitions { get; private set; }
    public int Extinguished { get; private set; }
    public int LostBlocks { get; private set; }
    public int LostBuildings { get; private set; }

    // Occupant ids of the placed buildings that burnt down in the last Step.
    public IReadOnlyList<int> DestroyedBuildings => m_Destroyed;

    public FireSystem(DisasterSystem owner, GridData grid, BalanceConfig config, CivicSystem civic, WaterSystem water,
        Func<TechModifiers> tech, Func<int> age, SimRandom random)
    {
        m_Owner = owner;
        m_Grid = grid;
        m_Config = config;
        m_Civic = civic;
        m_Water = water;
        m_Tech = tech;
        m_Age = age;
        m_Random = random;
    }

    private int Width => m_Grid.Width;
    private Vector2Int CellOf(int index) => new Vector2Int(index % Width, index / Width);
    private int IndexOf(Vector2Int cell) => cell.y * Width + cell.x;

    public bool IsBurning(Vector2Int cell) => m_Grid.InBounds(cell) && m_Owner.Fires[IndexOf(cell)] > 0;

    // Days the cell's fire has burnt (1 = lit today / yesterday); 0 = not burning.
    public int FireDays(Vector2Int cell) => m_Grid.InBounds(cell) ? m_Owner.Fires[IndexOf(cell)] : 0;

    public int BurningCount
    {
        get
        {
            int count = 0;
            foreach (byte days in m_Owner.Fires) if (days > 0) count++;
            return count;
        }
    }

    // True if the cell was a footprint cell of a placed building destroyed in the last Step.
    public bool WasDestroyed(Vector2Int cell) => m_Grid.InBounds(cell) && m_DestroyedCells.Contains(IndexOf(cell));

    // A grown block or a placed building stands here (roads and empty land don't burn).
    public bool CanBurn(Vector2Int cell)
    {
        return m_Grid.InBounds(cell) && !m_Grid.IsRoad(cell) && (m_Grid.IsOccupied(cell) || m_Grid.GetBuildingLevel(cell) > 0);
    }

    // Lights the building on a cell (a placed building burns as a whole). False if nothing can burn there, it is
    // already burning or the sim is not running disasters. Used by the daily ignition, tests and the DEBUG panel.
    public bool Ignite(Vector2Int cell)
    {
        if (!CanBurn(cell) || IsBurning(cell)) return false;
        LightUnit(IndexOf(cell));
        return true;
    }

    private void LightUnit(int index)
    {
        Ignitions++;
        int occupant = m_Grid.GetOccupant(CellOf(index));
        if (occupant == 0)
        {
            m_Owner.Fires[index] = 1;
            return;
        }
        foreach (int cell in CellsOf(occupant)) m_Owner.Fires[cell] = 1;
    }

    // Every cell of a placed building, row-major (the map is scanned once per Step, and only when a fire needs it).
    private List<int> CellsOf(int occupant)
    {
        if (m_OccupantCells == null)
        {
            m_OccupantCells = new Dictionary<int, List<int>>();
            int count = m_Grid.Width * m_Grid.Height;
            for (int i = 0; i < count; i++)
            {
                int id = m_Grid.GetOccupant(CellOf(i));
                if (id == 0) continue;
                if (!m_OccupantCells.TryGetValue(id, out List<int> cells)) m_OccupantCells[id] = cells = new List<int>();
                cells.Add(i);
            }
        }
        return m_OccupantCells.TryGetValue(occupant, out List<int> found) ? found : new List<int>();
    }

    // Clears this step's results (the switch is off, so nothing burns).
    public void ResetResults()
    {
        Ignitions = 0;
        Extinguished = 0;
        LostBlocks = 0;
        LostBuildings = 0;
        m_Destroyed.Clear();
        m_DestroyedCells.Clear();
        m_Processed.Clear();
        m_OccupantCells = null;
    }

    public void Step()
    {
        ResetResults();

        byte[] fires = m_Owner.Fires;
        DecayRubble();

        // The fires that were already burning when the day began: only these act today.
        m_Snapshot.Clear();
        for (int i = 0; i < fires.Length; i++)
        {
            if (fires[i] > 0) m_Snapshot.Add(i);
        }

        TryIgnite();

        foreach (int i in m_Snapshot)
        {
            if (fires[i] == 0) continue;     // already handled as part of its building
            Vector2Int cell = CellOf(i);
            int occupant = m_Grid.GetOccupant(cell);
            bool grown = occupant == 0 && m_Grid.GetBuildingLevel(cell) > 0;
            if (occupant == 0 && !grown)
            {
                fires[i] = 0;                // demolished while it burnt
                continue;
            }

            List<int> unit;
            if (occupant != 0)
            {
                if (!m_Processed.Add(occupant)) continue;
                unit = CellsOf(occupant);
            }
            else
            {
                m_Single.Clear();
                m_Single.Add(i);
                unit = m_Single;
            }
            ProcessUnit(unit, occupant);
        }
    }

    private void DecayRubble()
    {
        byte[] rubble = m_Owner.Rubble;
        for (int i = 0; i < rubble.Length; i++)
        {
            if (rubble[i] > 0) rubble[i]--;
        }
    }

    // One fire a day at most: the chance is 1 - exp(-FireIgnitionPerRisk x the grown blocks' summed fire risk) and
    // the block is picked in proportion to its risk. Nothing is drawn while no block has any risk (a young town).
    private void TryIgnite()
    {
        float ramp = m_Civic.Ramp;
        if (ramp <= 0f) return;

        byte[] fires = m_Owner.Fires;
        int count = m_Grid.Width * m_Grid.Height;
        float total = 0f;
        for (int i = 0; i < count; i++)
        {
            if (fires[i] != 0) continue;
            total += m_Civic.GrownFireRisk(CellOf(i), ramp);
        }
        if (total <= 0f) return;

        float chance = 1f - (float)Math.Exp(-m_Config.FireIgnitionPerRisk * total);
        if (!m_Random.Chance(chance)) return;

        float pick = m_Random.NextFloat() * total;
        int last = -1;
        float sum = 0f;
        for (int i = 0; i < count; i++)
        {
            if (fires[i] != 0) continue;
            float risk = m_Civic.GrownFireRisk(CellOf(i), ramp);
            if (risk <= 0f) continue;
            last = i;
            sum += risk;
            if (sum >= pick) break;
        }
        if (last >= 0) LightUnit(last);
    }

    private void ProcessUnit(List<int> unit, int occupant)
    {
        byte[] fires = m_Owner.Fires;
        int anchor = unit[0];
        Vector2Int anchorCell = CellOf(anchor);

        // Fire cover (and water) puts it out.
        float cover = m_Civic.Cover.GetStrength(ServiceKind.Fire, anchorCell);
        float extinguish = Mathf.Min(0.95f, m_Config.FireExtinguishBase + cover * m_Config.FireExtinguishPerCover);
        if (m_Water != null && m_Water.Mode != WaterRule.None && !m_Water.HasWater(anchorCell)) extinguish *= m_Config.DryExtinguishFactor;
        if (m_Random.Chance(extinguish))
        {
            foreach (int c in unit) fires[c] = 0;
            Extinguished++;
            return;
        }

        foreach (int c in unit) SpreadFrom(c);

        int days = fires[anchor] + 1;
        if (days > m_Config.FireBurnDays)
        {
            BurnDown(unit, occupant);
            return;
        }
        foreach (int c in unit) fires[c] = (byte)days;
    }

    private void SpreadFrom(int index)
    {
        Vector2Int cell = CellOf(index);
        float hazard = (m_Tech?.Invoke() ?? TechModifiers.None).Hazard(HazardKind.FireSpread);
        for (int dir = 0; dir < 4; dir++)
        {
            Vector2Int next = new Vector2Int(cell.x + s_Dx[dir], cell.y + s_Dy[dir]);
            if (!m_Grid.InBounds(next)) continue;

            float factor = 1f;
            Vector2Int target = next;
            if (m_Grid.IsRoad(next))
            {
                // A one-cell street: the fire may jump to the building on its far side.
                target = new Vector2Int(next.x + s_Dx[dir], next.y + s_Dy[dir]);
                if (!m_Grid.InBounds(target)) continue;
                factor = m_Config.FireJumpFactor;
            }
            if (!CanBurn(target) || IsBurning(target)) continue;

            float chance = m_Config.FireSpreadChance * factor * Flammability(target) * hazard
                * (1f - m_Civic.Cover.GetStrength(ServiceKind.Fire, target));
            if (m_Random.Chance(chance)) LightTarget(target);
        }
    }

    private void LightTarget(Vector2Int target)
    {
        Ignitions++;
        int occupant = m_Grid.GetOccupant(target);
        if (occupant == 0) m_Owner.Fires[IndexOf(target)] = 1;
        else foreach (int c in CellsOf(occupant)) m_Owner.Fires[c] = 1;
    }

    // How readily the building burns relative to a level of the baseline fire risk: a grown block by its built
    // age (and industry), a placed building at PlacedFlammability of the current age's.
    private float Flammability(Vector2Int cell)
    {
        float baseline = Mathf.Max(0.0001f, m_Config.FireRisk);
        if (m_Grid.IsOccupied(cell)) return m_Config.PlacedFlammability * m_Civic.FireBaseOfAge(m_Age()) / baseline;
        float risk = m_Civic.FireBaseOfAge(m_Grid.GetBuiltAge(cell));
        if (m_Grid.GetZone(cell) == ZoneType.Industrial) risk *= m_Config.FireRiskIndustrialFactor;
        return risk / baseline;
    }

    private void BurnDown(List<int> unit, int occupant)
    {
        byte rubble = (byte)Mathf.Clamp(m_Config.RubbleDays, 0, 254);
        byte[] fires = m_Owner.Fires;
        if (occupant == 0)
        {
            int i = unit[0];
            fires[i] = 0;
            m_Grid.SetBuildingLevel(CellOf(i), 0);
            m_Owner.Rubble[i] = rubble;
            LostBlocks++;
            return;
        }

        foreach (int c in unit)
        {
            fires[c] = 0;
            m_Grid.Release(CellOf(c), Vector2Int.one, 0);
            m_Owner.Rubble[c] = rubble;
            m_DestroyedCells.Add(c);
        }
        m_Destroyed.Add(occupant);
        LostBuildings++;
    }
}
