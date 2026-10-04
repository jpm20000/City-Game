using System;

// Presentation maths for the day/night cycle (M18d). Pure and never called by the simulation tick; it
// lives here so it is unit-tested. Phase 0 = dawn, 0.25 = noon, 0.5 = dusk, 0.75 = midnight, so a city
// always starts its first month in the morning.
public static class CalendarMath
{
    // Position in the cycle (0..1) from the calendar day (1-based), the fraction of the current day
    // that has elapsed (0..1) and the cycle length in days. Wraps, so days past the cycle length repeat.
    public static float DayPhase(int day, float dayFraction, int daysPerCycle)
    {
        if (daysPerCycle < 1) daysPerCycle = 1;
        float days = Math.Max(0, day - 1) + Math.Min(Math.Max(dayFraction, 0f), 0.9999f);
        float phase = days / daysPerCycle;
        return phase - (float)Math.Floor(phase);
    }

    // Sun height, -1 (midnight) .. 1 (noon).
    public static float SunHeight(float phase)
    {
        return (float)Math.Sin(phase * 2.0 * Math.PI);
    }

    // 0 in full daylight, 1 at night, smooth through dawn and dusk (symmetric around them). A positive
    // bias gives the day more of the cycle (the transition moves to a lower sun).
    public static float NightFactor(float phase, float bias = 0f)
    {
        float t = (SunHeight(phase) + bias + 0.25f) / 0.5f;
        t = Math.Min(Math.Max(t, 0f), 1f);
        float day = t * t * (3f - 2f * t);
        return 1f - day;
    }
}
