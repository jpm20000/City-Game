using System;
using System.Collections.Generic;
using UnityEngine;

// Breakdowns (M17): power plants, water towers and pumping stations (any placed source with a power or water
// supply; wells and fountains work by coverage and never break) go down now and then and give nothing for
// BreakdownDays unless the player pays RepairCostFraction of their cost. Per day at most one source breaks: the
// chance is 1 - exp(-lambda) with lambda the sum over working sources of BreakdownPerDay x (1 + BreakdownPerAgeBehind x
// ages its tech is behind) / funding^2 of its budget line x the techs' Breakdown multiplier; the source is picked in
// proportion to its weight (candidates sorted row-major by origin, so the sources' list order doesn't matter).
// Broken sources are listed in DisasterSystem.Broken by origin cell and fed to the power / water networks with a
// zero supply (Apply); Changed asks the sim to re-feed them.
public sealed class BreakdownSystem
{
    private readonly DisasterSystem m_Owner;
    private readonly BalanceConfig m_Config;
    private readonly Func<IReadOnlyList<ServiceSource>> m_Sources;
    private readonly BudgetSystem m_Budget;
    private readonly Func<TechModifiers> m_Tech;
    private readonly Func<int> m_Age;
    private readonly SimRandom m_Random;
    private readonly List<ServiceSource> m_Candidates = new();

    public event Action Changed;

    // This step's results: the source that broke (Origin, valid when Broke) and how many were repaired by time.
    public bool Broke { get; private set; }
    public Vector2Int BrokeOrigin { get; private set; }
    public int Recovered { get; private set; }

    public BreakdownSystem(DisasterSystem owner, BalanceConfig config, Func<IReadOnlyList<ServiceSource>> sources,
        BudgetSystem budget, Func<TechModifiers> tech, Func<int> age, SimRandom random)
    {
        m_Owner = owner;
        m_Config = config;
        m_Sources = sources;
        m_Budget = budget;
        m_Tech = tech;
        m_Age = age;
        m_Random = random;
    }

    public int Count => m_Owner.Broken.Count;
    public bool AnyBroken => m_Owner.Broken.Count > 0;

    public static bool CanBreak(in ServiceSource source) => source.PowerSupply > 0 || source.WaterSupply > 0;

    public bool IsBroken(Vector2Int origin) => IndexOf(origin) >= 0;

    // Days until a broken source works again (0 = working).
    public int DaysLeft(Vector2Int origin)
    {
        int i = IndexOf(origin);
        return i >= 0 ? m_Owner.Broken[i].DaysLeft : 0;
    }

    private int IndexOf(Vector2Int origin)
    {
        List<BrokenRecord> broken = m_Owner.Broken;
        for (int i = 0; i < broken.Count; i++)
        {
            if (broken[i].X == origin.x && broken[i].Y == origin.y) return i;
        }
        return -1;
    }

    // Takes a working source down (tests, DEBUG). False if no breakable source stands at that origin or it is down.
    public bool Break(Vector2Int origin)
    {
        if (IsBroken(origin)) return false;
        foreach (ServiceSource source in m_Sources())
        {
            if (source.Origin != origin || !CanBreak(source)) continue;
            m_Owner.Broken.Add(new BrokenRecord(origin.x, origin.y, Mathf.Max(1, m_Config.BreakdownDays)));
            Changed?.Invoke();
            return true;
        }
        return false;
    }

    // Repairs without charging (the sim's Repair charges first). False if it wasn't broken.
    public bool Repair(Vector2Int origin)
    {
        int i = IndexOf(origin);
        if (i < 0) return false;
        m_Owner.Broken.RemoveAt(i);
        Changed?.Invoke();
        return true;
    }

    public float RepairCost(in ServiceSource source) => source.Cost * m_Config.RepairCostFraction;

    // The sources with every broken one's supply set to 0 (the list itself when nothing is broken).
    public IReadOnlyList<ServiceSource> Apply(IReadOnlyList<ServiceSource> sources)
    {
        if (!AnyBroken) return sources;
        var result = new List<ServiceSource>(sources.Count);
        foreach (ServiceSource source in sources)
        {
            result.Add(IsBroken(source.Origin) && CanBreak(source) ? source.WithSupply(0, 0) : source);
        }
        return result;
    }

    // Drops records whose source no longer stands (it burnt down, was demolished, or the save has none there).
    // Returns how many were dropped.
    public int Prune(IReadOnlyList<ServiceSource> sources)
    {
        int dropped = 0;
        List<BrokenRecord> broken = m_Owner.Broken;
        for (int i = broken.Count - 1; i >= 0; i--)
        {
            bool found = false;
            foreach (ServiceSource source in sources)
            {
                if (source.Origin.x == broken[i].X && source.Origin.y == broken[i].Y && CanBreak(source))
                {
                    found = true;
                    break;
                }
            }
            if (found) continue;
            broken.RemoveAt(i);
            dropped++;
        }
        return dropped;
    }

    public void Step()
    {
        Broke = false;
        Recovered = 0;
        bool changed = Prune(m_Sources()) > 0;

        List<BrokenRecord> broken = m_Owner.Broken;
        for (int i = broken.Count - 1; i >= 0; i--)
        {
            BrokenRecord record = broken[i];
            record.DaysLeft--;
            if (record.DaysLeft <= 0)
            {
                broken.RemoveAt(i);
                Recovered++;
                changed = true;
            }
            else
            {
                broken[i] = record;
            }
        }

        // Row-major by origin, so the order the sources were placed in never matters.
        m_Candidates.Clear();
        foreach (ServiceSource source in m_Sources())
        {
            if (CanBreak(source) && !IsBroken(source.Origin)) m_Candidates.Add(source);
        }
        m_Candidates.Sort((a, b) => a.Origin.y != b.Origin.y ? a.Origin.y.CompareTo(b.Origin.y) : a.Origin.x.CompareTo(b.Origin.x));

        float total = 0f;
        foreach (ServiceSource source in m_Candidates) total += Weight(source);
        if (total > 0f && m_Random.Chance(1f - (float)Math.Exp(-total)))
        {
            float pick = m_Random.NextFloat() * total;
            float sum = 0f;
            ServiceSource chosen = m_Candidates[m_Candidates.Count - 1];
            foreach (ServiceSource source in m_Candidates)
            {
                sum += Weight(source);
                if (sum >= pick)
                {
                    chosen = source;
                    break;
                }
            }
            broken.Add(new BrokenRecord(chosen.Origin.x, chosen.Origin.y, Mathf.Max(1, m_Config.BreakdownDays)));
            Broke = true;
            BrokeOrigin = chosen.Origin;
            changed = true;
        }

        if (changed) Changed?.Invoke();
    }

    // The daily breakdown rate of one working source.
    public float Weight(in ServiceSource source)
    {
        int age = m_Age();
        int behind = source.TechAge >= 0 ? Mathf.Max(0, age - source.TechAge) : 0;
        float funding = 1f;
        BudgetLine? line = BudgetSystem.LineOf(source);
        if (line.HasValue && m_Budget != null) funding = Mathf.Max(0.1f, m_Budget.GetFunding(line.Value));
        float hazard = (m_Tech?.Invoke() ?? TechModifiers.None).Hazard(HazardKind.Breakdown);
        return m_Config.BreakdownPerDay * (1f + m_Config.BreakdownPerAgeBehind * behind) / (funding * funding) * hazard;
    }
}
