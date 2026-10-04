using System;
using System.Collections.Generic;
using UnityEngine;

// Funding per budget line (M15): 50-150% in steps, default 100% (identity: every effect and cost is
// exactly as it was before M15). A line's funding scales its buildings upkeep directly and, with
// diminishing returns, their effect and reach (EffectFactor / ReachFactor). Fund() returns the
// funded copy of a placed source; SimulationSystem feeds those copies to the spatial systems.
public sealed class BudgetSystem
{
    private static readonly int LineCount = Enum.GetValues(typeof(BudgetLine)).Length;

    private readonly BalanceConfig m_Config;
    private readonly float[] m_Funding;

    // Raised after any funding change (SimulationSystem re-feeds the funded sources).
    public event Action Changed;

    public BudgetSystem(BalanceConfig config)
    {
        m_Config = config ?? throw new ArgumentNullException(nameof(config));
        m_Funding = new float[LineCount];
        for (int i = 0; i < LineCount; i++) m_Funding[i] = 1f;
    }

    public static int Lines => LineCount;

    public float GetFunding(BudgetLine line) => m_Funding[(int)line];

    // Clamped to FundingMin..FundingMax and snapped to FundingStep.
    public void SetFunding(BudgetLine line, float funding)
    {
        float snapped = Mathf.Round(funding / m_Config.FundingStep) * m_Config.FundingStep;
        snapped = Mathf.Clamp(snapped, m_Config.FundingMin, m_Config.FundingMax);
        if (Mathf.Approximately(snapped, m_Funding[(int)line])) return;
        m_Funding[(int)line] = snapped;
        Changed?.Invoke();
    }

    // True while every line is at 100% (the funded sources are then the placed sources themselves).
    public bool IsDefault
    {
        get
        {
            for (int i = 0; i < LineCount; i++)
            {
                if (m_Funding[i] != 1f) return false;
            }
            return true;
        }
    }

    // Load / new game: sets every line (missing entries stay 100%) and raises Changed once.
    public void Restore(IReadOnlyList<float> funding)
    {
        for (int i = 0; i < LineCount; i++)
        {
            float f = funding != null && i < funding.Count ? funding[i] : 1f;
            m_Funding[i] = f <= 0f ? 1f : Mathf.Clamp(f, m_Config.FundingMin, m_Config.FundingMax);
        }
        Changed?.Invoke();
    }

    public float[] ExportFunding() => (float[])m_Funding.Clone();

    // Effect (strength, supply, park cheer, school RP): f up to 100%, half as much above.
    public float EffectFactor(BudgetLine line) => EffectFactorAt(m_Config, m_Funding[(int)line]);

    // Reach: half the effect's departure from 100%.
    public float ReachFactor(BudgetLine line) => ReachFactorAt(m_Config, m_Funding[(int)line]);

    public static float EffectFactorAt(BalanceConfig config, float funding)
    {
        return funding <= 1f ? funding : 1f + config.FundingOverSlope * (funding - 1f);
    }

    public static float ReachFactorAt(BalanceConfig config, float funding)
    {
        return 1f + config.FundingReachSlope * (EffectFactorAt(config, funding) - 1f);
    }

    // The line a placed building upkeep is charged to: civic > power > water > parks; null = none.
    public static BudgetLine? LineOf(in ServiceSource source)
    {
        switch (source.CivicKind)
        {
            case ServiceKind.Order: return BudgetLine.Order;
            case ServiceKind.Fire: return BudgetLine.Fire;
            case ServiceKind.Health: return BudgetLine.Health;
            case ServiceKind.Education: return BudgetLine.Education;
        }
        if (source.PowerSupply > 0) return BudgetLine.Power;
        if (source.WaterSupply > 0 || source.WaterRadius > 0) return BudgetLine.Water;
        if (source.CoverageRadius > 0) return BudgetLine.Parks;
        return null;
    }

    // The source as the spatial systems should see it: each effect follows its own line (park reach
    // -> Parks, plant supply -> Power, tower supply and well reach -> Water, civic reach and
    // strength -> the civic line). Pollution is not funded.
    public ServiceSource Fund(in ServiceSource s)
    {
        int coverage = ScaleReach(s.CoverageRadius, BudgetLine.Parks);
        int power = ScaleSupply(s.PowerSupply, BudgetLine.Power);
        int waterSupply = ScaleSupply(s.WaterSupply, BudgetLine.Water);
        int waterRadius = ScaleReach(s.WaterRadius, BudgetLine.Water);
        int civicRadius = s.CivicRadius;
        float civicStrength = s.CivicStrength;
        if (s.CivicKind != ServiceKind.None)
        {
            BudgetLine line = LineOf(s).Value;
            civicRadius = ScaleReach(s.CivicRadius, line);
            civicStrength = Mathf.Min(1f, s.CivicStrength * EffectFactor(line));
        }
        return new ServiceSource(s.Origin, s.Size, coverage, power, s.Pollution, s.PollutionRadius, waterSupply, waterRadius,
            s.CivicKind, civicRadius, civicStrength, s.UpkeepPerDay, s.ResearchPerDay);
    }

    public List<ServiceSource> FundAll(IReadOnlyList<ServiceSource> sources)
    {
        var funded = new List<ServiceSource>(sources.Count);
        foreach (ServiceSource s in sources) funded.Add(Fund(s));
        return funded;
    }

    private int ScaleReach(int radius, BudgetLine line)
    {
        return radius <= 0 ? radius : Mathf.Max(1, Mathf.RoundToInt(radius * ReachFactor(line)));
    }

    private int ScaleSupply(int supply, BudgetLine line)
    {
        return supply <= 0 ? supply : Mathf.Max(1, Mathf.RoundToInt(supply * EffectFactor(line)));
    }

    // Placed buildings upkeep at today's funding: fills upkeepByLine with each line's funded upkeep,
    // returns the sum of all sources 100% upkeep and the total change funding makes to it.
    public float Accumulate(IReadOnlyList<ServiceSource> sources, float[] upkeepByLine, out float fundingDelta)
    {
        float baseSum = 0f;
        float delta = 0f;
        Array.Clear(upkeepByLine, 0, upkeepByLine.Length);
        foreach (ServiceSource s in sources)
        {
            if (s.UpkeepPerDay == 0f) continue;
            baseSum += s.UpkeepPerDay;
            BudgetLine? line = LineOf(s);
            if (line == null) continue;
            float f = m_Funding[(int)line.Value];
            upkeepByLine[(int)line.Value] += s.UpkeepPerDay * f;
            delta += s.UpkeepPerDay * (f - 1f);
        }
        fundingDelta = delta;
        return baseSum;
    }

    // Research points per day the funded schools add or lose against their 100% rate.
    public float ResearchDelta(IReadOnlyList<ServiceSource> sources)
    {
        float effect = EffectFactor(BudgetLine.Education);
        if (effect == 1f) return 0f;
        float delta = 0f;
        foreach (ServiceSource s in sources)
        {
            if (s.ResearchPerDay > 0f && LineOf(s) == BudgetLine.Education) delta += s.ResearchPerDay * (effect - 1f);
        }
        return delta;
    }
}
