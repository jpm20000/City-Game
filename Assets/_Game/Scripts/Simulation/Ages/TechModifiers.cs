using System;
using System.Collections.Generic;

// The effects of every researched tech folded together. Immutable; TechSystem rebuilds it when a
// tech completes or research state is restored. A class rather than a struct so the default is
// the identity (multipliers 1), not zeros.
public sealed class TechModifiers
{
    public static readonly TechModifiers None = new TechModifiers();

    private readonly float[] m_Demand = { 1f, 1f, 1f };   // R, C, I
    private readonly HashSet<string> m_Unlocked = new();

    public float ResearchMultiplier { get; private set; } = 1f;
    public float HappinessBonus { get; private set; }
    public float UpkeepMultiplier { get; private set; } = 1f;
    public IReadOnlyCollection<string> UnlockedBuildings => m_Unlocked;

    private TechModifiers() { }

    public float DemandMultiplier(ZoneType zone)
    {
        int i = (int)zone - 1;
        return i >= 0 && i < m_Demand.Length ? m_Demand[i] : 1f;
    }

    public bool IsUnlocked(string buildingId) => !string.IsNullOrEmpty(buildingId) && m_Unlocked.Contains(buildingId);

    public static TechModifiers Fold(IEnumerable<TechDefinition> researched)
    {
        var result = new TechModifiers();
        if (researched == null) return result;

        foreach (TechDefinition tech in researched)
        {
            if (tech == null) continue;
            foreach (TechEffect effect in tech.Effects)
            {
                switch (effect.Type)
                {
                    case TechEffectType.UnlockBuilding:
                        if (!string.IsNullOrEmpty(effect.Target)) result.m_Unlocked.Add(effect.Target);
                        break;
                    case TechEffectType.ResearchMultiplier:
                        result.ResearchMultiplier *= effect.Value;
                        break;
                    case TechEffectType.HappinessBonus:
                        result.HappinessBonus += effect.Value;
                        break;
                    case TechEffectType.UpkeepMultiplier:
                        result.UpkeepMultiplier *= effect.Value;
                        break;
                    case TechEffectType.DemandMultiplier:
                        if (string.IsNullOrEmpty(effect.Target))
                        {
                            for (int i = 0; i < result.m_Demand.Length; i++) result.m_Demand[i] *= effect.Value;
                        }
                        else if (Enum.TryParse(effect.Target, true, out ZoneType zone) && zone != ZoneType.None)
                        {
                            result.m_Demand[(int)zone - 1] *= effect.Value;
                        }
                        break;
                }
            }
        }
        return result;
    }
}
