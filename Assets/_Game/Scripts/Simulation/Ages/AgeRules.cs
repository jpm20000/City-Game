// What the sim needs to know about an age's growth rules.
public readonly struct AgeRules
{
    // The no-ages path: today's game (Industrial rules, Electricity researched; piped water since M13).
    public static readonly AgeRules Legacy = new AgeRules(3, 1f, true, WaterRule.Piped);

    public readonly int MaxLevel;
    public readonly float CapacityScale;
    public readonly bool UpgradesNeedPower;
    public readonly WaterRule Water;

    public AgeRules(int maxLevel, float capacityScale, bool upgradesNeedPower, WaterRule water = WaterRule.None)
    {
        MaxLevel = maxLevel;
        CapacityScale = capacityScale;
        UpgradesNeedPower = upgradesNeedPower;
        Water = water;
    }
}
