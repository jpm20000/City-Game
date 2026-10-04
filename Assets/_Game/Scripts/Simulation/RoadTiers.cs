using System;

// Lookup of the road tiers' numbers (M16), by tier byte 1..5. Built from the TechDatabase's tier
// assets, or "legacy" without one: every tier is today's Paved road from BalanceConfig, so the
// age-less sim plays exactly as before. Which tiers are unlocked comes from the researched techs.
public sealed class RoadTiers
{
    public const int Count = GridData.MaxRoadTier;
    public const byte Dirt = 1, Cobble = 2, Paved = 3, Avenue = 4, Highway = 5;

    private readonly RoadTierDefinition[] m_Defs = new RoadTierDefinition[Count + 1];   // null = legacy
    private readonly BalanceConfig m_Config;
    private readonly Func<TechDefinition, bool> m_IsResearched;

    // techs null / without RoadTiers = legacy. isResearched answers for a tier's required tech.
    public RoadTiers(BalanceConfig config, TechDatabase techs = null, Func<TechDefinition, bool> isResearched = null)
    {
        m_Config = config ?? throw new ArgumentNullException(nameof(config));
        m_IsResearched = isResearched ?? (_ => true);
        if (techs == null) return;
        foreach (RoadTierDefinition tier in techs.RoadTiers)
        {
            if (tier != null && tier.Tier >= 1 && tier.Tier <= Count) m_Defs[tier.Tier] = tier;
        }
    }

    // True when built from tier assets (false = every tier is the legacy Paved road).
    public bool HasContent
    {
        get
        {
            for (int i = 1; i <= Count; i++)
            {
                if (m_Defs[i] != null) return true;
            }
            return false;
        }
    }

    public RoadTierDefinition Definition(int tier) => tier >= 1 && tier <= Count ? m_Defs[tier] : null;

    public int Cost(int tier)
    {
        RoadTierDefinition def = Definition(tier);
        return def != null ? def.Cost : m_Config.RoadCost;
    }

    public float UpkeepPerDay(int tier)
    {
        RoadTierDefinition def = Definition(tier);
        return def != null ? def.UpkeepPerDay : m_Config.RoadUpkeepPerDay;
    }

    public float Capacity(int tier)
    {
        RoadTierDefinition def = Definition(tier);
        return def != null ? def.Capacity : m_Config.RoadCapacity;
    }

    public int TravelCost(int tier)
    {
        RoadTierDefinition def = Definition(tier);
        return def != null ? def.TravelCost : m_Config.RoadTravelCost;
    }

    public bool Frontage(int tier)
    {
        RoadTierDefinition def = Definition(tier);
        return def == null || def.Frontage;
    }

    public string DisplayName(int tier)
    {
        RoadTierDefinition def = Definition(tier);
        return def != null ? def.DisplayName : "Road";
    }

    // Legacy tiers are always unlocked; a tier with no tech (dirt) too.
    public bool IsUnlocked(int tier)
    {
        if (tier < 1 || tier > Count) return false;
        RoadTierDefinition def = m_Defs[tier];
        return def == null || def.RequiredTech == null || m_IsResearched(def.RequiredTech);
    }

    // The tier the Road tool lays: the best unlocked tier with frontage that no unlocked tier replaces
    // in the toolbar. Paved in the legacy sim.
    public byte BestStreetTier => HasContent ? BestStreet(m_Defs, m_IsResearched) : Paved;

    // The same rule from a database and a researched test (the save migration has no sim yet).
    public static byte BestStreet(TechDatabase techs, Func<TechDefinition, bool> isResearched)
    {
        var defs = new RoadTierDefinition[Count + 1];
        foreach (RoadTierDefinition tier in techs.RoadTiers)
        {
            if (tier != null && tier.Tier >= 1 && tier.Tier <= Count) defs[tier.Tier] = tier;
        }
        return BestStreet(defs, isResearched);
    }

    private static byte BestStreet(RoadTierDefinition[] defs, Func<TechDefinition, bool> isResearched)
    {
        bool Unlocked(int t) => defs[t] != null && (defs[t].RequiredTech == null || isResearched(defs[t].RequiredTech));
        byte best = 0;
        for (byte t = 1; t <= Paved; t++)     // street tiers; Avenue and Highway have their own tools
        {
            RoadTierDefinition def = defs[t];
            if (!Unlocked(t)) continue;
            if (def.ObsoleteBy != 0 && Unlocked(def.ObsoleteBy)) continue;
            best = t;
        }
        return best != 0 ? best : Paved;
    }

    // Price of upgrading a road from one tier to another: the difference, never negative.
    public int UpgradeCost(int from, int to) => Math.Max(0, Cost(to) - Cost(from));

    // Sum of every road's daily upkeep (before the tech upkeep multiplier).
    public float UpkeepPerDay(GridData grid)
    {
        var counts = new int[Count + 1];
        grid.CountRoadsByTier(counts);
        float total = 0f;
        for (int t = 1; t <= Count; t++) total += counts[t] * UpkeepPerDay(t);
        return total;
    }
}
