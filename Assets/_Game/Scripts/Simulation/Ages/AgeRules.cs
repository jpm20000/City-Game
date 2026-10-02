// What the sim needs to know about an age's growth rules.
public readonly struct AgeRules
{
    // The no-ages path: today's game (Industrial rules, Electricity researched).
    public static readonly AgeRules Legacy = new AgeRules(3, 1f, true);

    public readonly int MaxLevel;
    public readonly float CapacityScale;
    public readonly bool UpgradesNeedPower;

    public AgeRules(int maxLevel, float capacityScale, bool upgradesNeedPower)
    {
        MaxLevel = maxLevel;
        CapacityScale = capacityScale;
        UpgradesNeedPower = upgradesNeedPower;
    }
}
