using System;
using System.Collections.Generic;

// The effects of every researched tech and enacted ordinance folded together. Immutable; TechSystem
// rebuilds it when a tech completes, an ordinance is enacted or repealed, or research state is
// restored. A class rather than a struct so the default is the identity (multipliers 1), not zeros.
public sealed class TechModifiers
{
    public static readonly TechModifiers None = new TechModifiers();

    private readonly float[] m_Demand = { 1f, 1f, 1f };   // R, C, I
    private readonly float[] m_CivicNeed = { 1f, 1f, 1f, 1f, 1f };   // by ServiceKind (M15): crime, fire risk, sickness
    private readonly HashSet<string> m_Unlocked = new();

    public float ResearchMultiplier { get; private set; } = 1f;
    public float HappinessBonus { get; private set; }
    public float OrdinanceHappiness { get; private set; }     // enacted ordinances' happiness (M15), apart from the techs'
    public float UpkeepMultiplier { get; private set; } = 1f;
    public float PollutionMultiplier { get; private set; } = 1f;
    public float LandValueBonus { get; private set; }
    public float LoanInterestMultiplier { get; private set; } = 1f;
    public float TrafficMultiplier { get; private set; } = 1f;      // commute trips x this (M16)
    public IReadOnlyCollection<string> UnlockedBuildings => m_Unlocked;

    private TechModifiers() { }

    public float DemandMultiplier(ZoneType zone)
    {
        int i = (int)zone - 1;
        return i >= 0 && i < m_Demand.Length ? m_Demand[i] : 1f;
    }

    // Crime (Order), fire risk (Fire) or sickness (Health) x this; 1 for the other lines.
    public float CivicNeed(ServiceKind kind)
    {
        int i = (int)kind;
        return i >= 0 && i < m_CivicNeed.Length ? m_CivicNeed[i] : 1f;
    }

    public bool IsUnlocked(string buildingId) => !string.IsNullOrEmpty(buildingId) && m_Unlocked.Contains(buildingId);

    public static TechModifiers Fold(IEnumerable<TechDefinition> researched, IEnumerable<OrdinanceDefinition> enacted = null)
    {
        var result = new TechModifiers();

        if (researched != null)
        {
            foreach (TechDefinition tech in researched)
            {
                if (tech == null) continue;
                foreach (TechEffect effect in tech.Effects) result.Apply(effect, false);
            }
        }
        if (enacted != null)
        {
            foreach (OrdinanceDefinition ordinance in enacted)
            {
                if (ordinance == null) continue;
                foreach (TechEffect effect in ordinance.Effects) result.Apply(effect, true);
            }
        }
        return result;
    }

    private void Apply(TechEffect effect, bool ordinance)
    {
        switch (effect.Type)
        {
            case TechEffectType.UnlockBuilding:
                if (!string.IsNullOrEmpty(effect.Target)) m_Unlocked.Add(effect.Target);
                break;
            case TechEffectType.ResearchMultiplier:
                ResearchMultiplier *= effect.Value;
                break;
            case TechEffectType.HappinessBonus:
                if (ordinance) OrdinanceHappiness += effect.Value;
                else HappinessBonus += effect.Value;
                break;
            case TechEffectType.UpkeepMultiplier:
                UpkeepMultiplier *= effect.Value;
                break;
            case TechEffectType.PollutionMultiplier:
                PollutionMultiplier *= effect.Value;
                break;
            case TechEffectType.LandValueBonus:
                LandValueBonus += effect.Value;
                break;
            case TechEffectType.LoanInterestMultiplier:
                LoanInterestMultiplier *= effect.Value;
                break;
            case TechEffectType.TrafficMultiplier:
                TrafficMultiplier *= effect.Value;
                break;
            case TechEffectType.CivicNeedMultiplier:
                if (Enum.TryParse(effect.Target, true, out ServiceKind kind) && kind != ServiceKind.None
                    && (int)kind < m_CivicNeed.Length)
                {
                    m_CivicNeed[(int)kind] *= effect.Value;
                }
                break;
            case TechEffectType.DemandMultiplier:
                if (string.IsNullOrEmpty(effect.Target))
                {
                    for (int i = 0; i < m_Demand.Length; i++) m_Demand[i] *= effect.Value;
                }
                else if (Enum.TryParse(effect.Target, true, out ZoneType zone) && zone != ZoneType.None)
                {
                    m_Demand[(int)zone - 1] *= effect.Value;
                }
                break;
        }
    }
}
