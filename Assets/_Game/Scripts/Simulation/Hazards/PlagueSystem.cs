using System;
using System.Collections.Generic;
using UnityEngine;

// Plague (M17), mostly a Medieval and Renaissance worry (AgeDefinition.PlagueRisk). With no outbreak running and the
// cooldown over, a town of at least PlagueMinPopulation residents has a daily chance of one that follows the homes'
// mean sickness (so health cover, Herb Gardens and Free Clinics count); patient zero is picked by capacity x
// sickness. While it runs, every infected home tries to infect each healthy home within PlagueRadius with
// PlagueSpread x the target's sickness x the techs' PlagueSpread multiplier, stays infected PlagueDays and is then
// immune until the outbreak ends; infected homes lose PlagueDeathRate of their residents a day (deterministic, the
// remainder carries). When no home is infected the outbreak ends and PlagueCooldownDays must pass. The layer is
// DisasterSystem.Plague (0 healthy, 1..PlagueDays days infected, PlagueRecovered immune). Draws: the outbreak
// chance (then the patient pick), or one per infected-home / healthy-target pair in row-major order.
public sealed class PlagueSystem
{
    private readonly DisasterSystem m_Owner;
    private readonly GridData m_Grid;
    private readonly BalanceConfig m_Config;
    private readonly CivicSystem m_Civic;
    private readonly CapacityModel m_Capacity;
    private readonly PopulationSystem m_Population;
    private readonly Func<TechModifiers> m_Tech;
    private readonly Func<float> m_Risk;
    private readonly SimRandom m_Random;
    private readonly List<int> m_Snapshot = new();
    private int m_Infected;

    // This step's results.
    public bool Started { get; private set; }
    public bool Ended { get; private set; }
    public int Died { get; private set; }

    public PlagueSystem(DisasterSystem owner, GridData grid, BalanceConfig config, CivicSystem civic, CapacityModel capacity,
        PopulationSystem population, Func<TechModifiers> tech, Func<float> risk, SimRandom random)
    {
        m_Owner = owner;
        m_Grid = grid;
        m_Config = config;
        m_Civic = civic;
        m_Capacity = capacity;
        m_Population = population;
        m_Tech = tech;
        m_Risk = risk;
        m_Random = random;
    }

    private int Width => m_Grid.Width;
    private Vector2Int CellOf(int index) => new Vector2Int(index % Width, index / Width);
    private int IndexOf(Vector2Int cell) => cell.y * Width + cell.x;

    // Homes infected right now (derived from the layer; refreshed by Step and Restore).
    public int InfectedCount => m_Infected;
    public bool Active => m_Infected > 0;

    // Residents lost to the outbreak running now (or the last one, until the next starts).
    public int OutbreakDeaths => m_Owner.PlagueDeaths;

    public bool IsInfected(Vector2Int cell)
    {
        if (!m_Grid.InBounds(cell)) return false;
        byte value = m_Owner.Plague[IndexOf(cell)];
        return value > 0 && value != DisasterSystem.PlagueRecovered;
    }

    public bool IsRecovered(Vector2Int cell) => m_Grid.InBounds(cell) && m_Owner.Plague[IndexOf(cell)] == DisasterSystem.PlagueRecovered;

    // The cell is a grown home (the only thing that can be infected).
    public bool IsHome(Vector2Int cell)
    {
        return m_Grid.InBounds(cell) && m_Grid.GetZone(cell) == ZoneType.Residential && m_Grid.GetBuildingLevel(cell) > 0
            && !m_Grid.IsOccupied(cell);
    }

    // Infects a home (tests, DEBUG, patient zero). False if it isn't a healthy grown home.
    public bool Infect(Vector2Int cell)
    {
        if (!IsHome(cell)) return false;
        int i = IndexOf(cell);
        if (m_Owner.Plague[i] != 0) return false;
        m_Owner.Plague[i] = 1;
        m_Infected++;
        return true;
    }

    // Recounts the infected homes from the layer (after a load or a clear).
    public void Recount()
    {
        int count = 0;
        foreach (byte value in m_Owner.Plague)
        {
            if (value > 0 && value != DisasterSystem.PlagueRecovered) count++;
        }
        m_Infected = count;
    }

    // Share of the city's housing that is infected, 0..1.
    public float InfectedHousingShare(int housing)
    {
        if (m_Infected == 0 || housing <= 0) return 0f;
        long infected = 0;
        byte[] layer = m_Owner.Plague;
        for (int i = 0; i < layer.Length; i++)
        {
            if (layer[i] == 0 || layer[i] == DisasterSystem.PlagueRecovered) continue;
            infected += m_Capacity.CapacityOf(m_Grid, CellOf(i));
        }
        return Mathf.Clamp01((float)infected / housing);
    }

    // The happiness term while an outbreak runs: -min(share x PlaguePenalty, PlaguePenaltyCap); 0 otherwise.
    public float HappinessTerm(int housing)
    {
        return -PenaltyAt(m_Config, InfectedHousingShare(housing));
    }

    public static float PenaltyAt(BalanceConfig config, float infectedShare)
    {
        return Mathf.Min(infectedShare * config.PlaguePenalty, config.PlaguePenaltyCap);
    }

    public void Step()
    {
        Started = false;
        Ended = false;
        Died = 0;
        byte[] layer = m_Owner.Plague;

        // Homes that burnt down or were demolished carry no infection.
        bool any = false;
        for (int i = 0; i < layer.Length; i++)
        {
            if (layer[i] == 0) continue;
            if (!IsHome(CellOf(i))) layer[i] = 0;
            else any = true;
        }
        Recount();

        if (!any)
        {
            m_Infected = 0;
            TryOutbreak();
            return;
        }
        if (m_Infected == 0)
        {
            End();       // only recovered homes are left
            return;
        }

        m_Snapshot.Clear();
        for (int i = 0; i < layer.Length; i++)
        {
            if (layer[i] > 0 && layer[i] != DisasterSystem.PlagueRecovered) m_Snapshot.Add(i);
        }

        Spread();
        Kill();

        byte limit = (byte)Mathf.Clamp(m_Config.PlagueDays, 1, 250);
        foreach (int i in m_Snapshot)
        {
            if (++layer[i] > limit) layer[i] = DisasterSystem.PlagueRecovered;
        }
        Recount();
        if (m_Infected == 0) End();
    }

    private void End()
    {
        Array.Clear(m_Owner.Plague, 0, m_Owner.Plague.Length);
        m_Infected = 0;
        m_Owner.PlagueCooldown = m_Config.PlagueCooldownDays;
        m_Owner.PlagueRemainder = 0f;
        Ended = true;
    }

    private void TryOutbreak()
    {
        if (m_Owner.PlagueCooldown > 0)
        {
            m_Owner.PlagueCooldown--;
            return;
        }
        float risk = m_Risk();
        if (risk <= 0f || m_Population.Population < m_Config.PlagueMinPopulation) return;

        float ramp = m_Civic.Ramp;
        double weightTotal = 0, housing = 0;
        int count = m_Grid.Width * m_Grid.Height;
        for (int i = 0; i < count; i++)
        {
            Vector2Int cell = CellOf(i);
            if (m_Grid.GetZone(cell) != ZoneType.Residential || m_Grid.GetBuildingLevel(cell) == 0) continue;
            int capacity = m_Capacity.CapacityOf(m_Grid, cell);
            housing += capacity;
            weightTotal += capacity * m_Civic.HomeSickness(cell, ramp);
        }
        if (housing <= 0 || weightTotal <= 0) return;

        float mean = (float)(weightTotal / housing);
        if (!m_Random.Chance(m_Config.PlagueOutbreakPerDay * risk * mean)) return;

        double pick = m_Random.NextFloat() * weightTotal;
        double sum = 0;
        int chosen = -1;
        for (int i = 0; i < count; i++)
        {
            Vector2Int cell = CellOf(i);
            if (m_Grid.GetZone(cell) != ZoneType.Residential || m_Grid.GetBuildingLevel(cell) == 0) continue;
            double weight = m_Capacity.CapacityOf(m_Grid, cell) * m_Civic.HomeSickness(cell, ramp);
            if (weight <= 0) continue;
            chosen = i;
            sum += weight;
            if (sum >= pick) break;
        }
        if (chosen < 0) return;

        m_Owner.Plague[chosen] = 1;
        m_Owner.PlagueDeaths = 0;
        m_Owner.PlagueRemainder = 0f;
        m_Infected = 1;
        Started = true;
    }

    private void Spread()
    {
        float ramp = m_Civic.Ramp;
        float hazard = (m_Tech?.Invoke() ?? TechModifiers.None).Hazard(HazardKind.PlagueSpread);
        int radius = Mathf.Max(0, m_Config.PlagueRadius);
        byte[] layer = m_Owner.Plague;
        foreach (int i in m_Snapshot)
        {
            Vector2Int cell = CellOf(i);
            for (int dy = -radius; dy <= radius; dy++)
            {
                for (int dx = -radius; dx <= radius; dx++)
                {
                    if (dx == 0 && dy == 0) continue;
                    var target = new Vector2Int(cell.x + dx, cell.y + dy);
                    if (!IsHome(target)) continue;
                    int t = IndexOf(target);
                    if (layer[t] != 0) continue;
                    float chance = m_Config.PlagueSpread * m_Civic.HomeSickness(target, ramp) * hazard;
                    if (m_Random.Chance(chance)) layer[t] = 1;
                }
            }
        }
    }

    // Infected homes' residents die at PlagueDeathRate a day; the fraction left over carries to the next day.
    private void Kill()
    {
        float occupancy = m_Population.Housing > 0 ? Mathf.Min(1f, (float)m_Population.Population / m_Population.Housing) : 0f;
        float residents = 0f;
        foreach (int i in m_Snapshot) residents += m_Capacity.CapacityOf(m_Grid, CellOf(i)) * occupancy;

        float deaths = residents * m_Config.PlagueDeathRate + m_Owner.PlagueRemainder;
        int whole = Mathf.FloorToInt(deaths);
        m_Owner.PlagueRemainder = deaths - whole;
        Died = m_Population.LoseResidents(whole);
        m_Owner.PlagueDeaths += Died;
    }
}
