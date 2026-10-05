using System.Reflection;

// M24d: goods are on from the Industrial age, which changes every Industrial number. Tests that compare an aged city with the
// age-less sim (the identity baseline) or with a recorded fixture switch them off through this.
internal static class GoodsOff
{
    public static void Apply(BalanceConfig config)
    {
        typeof(BalanceConfig).GetField("m_GoodsMinAge", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(config, 99);
    }
}
