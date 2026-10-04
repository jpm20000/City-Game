using System;
using System.Globalization;

// The sim's only source of randomness (M17): xorshift64* seeded through SplitMix64. Its whole state is one
// ulong, saved as a 16-digit hex string, so a restored city draws exactly the numbers the uninterrupted one would.
// Only DisasterSystem.Step draws from it, in a fixed order, and never per frame or per UI event.
public sealed class SimRandom
{
    private ulong m_State;

    public SimRandom(ulong seed = 1)
    {
        Seed(seed);
    }

    // The state as saved: 16 hex digits.
    public string StateString => m_State.ToString("x16", CultureInfo.InvariantCulture);

    public void Seed(ulong seed)
    {
        // SplitMix64 spreads small seeds (1, 2, 3...) over the whole state; xorshift must never hold 0.
        ulong z = seed + 0x9E3779B97F4A7C15UL;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        z ^= z >> 31;
        m_State = z == 0 ? 0x9E3779B97F4A7C15UL : z;
    }

    // Restores a saved state; false (and the state is untouched) for anything that isn't a non-zero hex ulong.
    public bool TryRestore(string state)
    {
        if (string.IsNullOrEmpty(state)) return false;
        if (!ulong.TryParse(state, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out ulong value)) return false;
        if (value == 0) return false;
        m_State = value;
        return true;
    }

    public ulong NextULong()
    {
        ulong x = m_State;
        x ^= x >> 12;
        x ^= x << 25;
        x ^= x >> 27;
        m_State = x;
        return x * 0x2545F4914F6CDD1DUL;
    }

    // [0, 1)
    public float NextFloat()
    {
        return (NextULong() >> 40) * (1f / (1 << 24));
    }

    // [0, max); 0 when max <= 0. Still draws once, so the draw count never depends on the arguments.
    public int NextInt(int max)
    {
        ulong value = NextULong();
        return max <= 0 ? 0 : (int)(value % (ulong)max);
    }

    // True with probability p (one draw, whatever p is).
    public bool Chance(float p)
    {
        return NextFloat() < p;
    }
}
