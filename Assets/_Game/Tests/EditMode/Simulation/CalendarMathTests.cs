using NUnit.Framework;

// M18a: the day/night phase maths (presentation only; the sim never reads it).
public sealed class CalendarMathTests
{
    [Test]
    public void Phase_StartsAtDawn_AndWrapsAtTheEndOfTheCycle()
    {
        Assert.AreEqual(0f, CalendarMath.DayPhase(1, 0f, 30), 1e-6f);
        Assert.AreEqual(15f / 30f, CalendarMath.DayPhase(16, 0f, 30), 1e-6f);
        Assert.AreEqual(0f, CalendarMath.DayPhase(31, 0f, 30), 1e-6f, "day 31 starts the next cycle");
        Assert.Less(CalendarMath.DayPhase(30, 0.9999f, 30), 1f);
    }

    [Test]
    public void Phase_AdvancesWithTheFractionOfTheDay()
    {
        float a = CalendarMath.DayPhase(10, 0.0f, 30);
        float b = CalendarMath.DayPhase(10, 0.5f, 30);
        float c = CalendarMath.DayPhase(11, 0.0f, 30);
        Assert.Less(a, b);
        Assert.Less(b, c);
        Assert.AreEqual(b - a, c - b, 1e-6f);
    }

    [Test]
    public void Phase_SurvivesBadInput()
    {
        Assert.AreEqual(0f, CalendarMath.DayPhase(0, -1f, 30), 1e-6f);
        float phase = CalendarMath.DayPhase(5, 2f, 0);
        Assert.GreaterOrEqual(phase, 0f);
        Assert.Less(phase, 1f);
    }

    [Test]
    public void NightFactor_IsDayAtNoon_NightAtMidnight_AndSymmetricAroundDawnAndDusk()
    {
        Assert.AreEqual(0f, CalendarMath.NightFactor(0.25f), 1e-6f);
        Assert.AreEqual(1f, CalendarMath.NightFactor(0.75f), 1e-6f);
        Assert.AreEqual(0.5f, CalendarMath.NightFactor(0f), 1e-5f);
        Assert.AreEqual(0.5f, CalendarMath.NightFactor(0.5f), 1e-5f);
        for (float d = 0f; d <= 0.1f; d += 0.01f)
        {
            Assert.AreEqual(CalendarMath.NightFactor(0.5f - d) - 0.5f, 0.5f - CalendarMath.NightFactor(0.5f + d), 1e-5f, "dusk is symmetric");
        }
    }

    [Test]
    public void NightFactor_RisesMonotonicallyFromNoonToMidnight()
    {
        float previous = CalendarMath.NightFactor(0.25f);
        for (float phase = 0.26f; phase <= 0.75f; phase += 0.01f)
        {
            float night = CalendarMath.NightFactor(phase);
            Assert.GreaterOrEqual(night, previous - 1e-6f, $"phase {phase}");
            previous = night;
        }
    }

    [Test]
    public void DayBias_GivesTheDayMoreOfTheCycle_WithoutChangingNoonOrMidnight()
    {
        Assert.AreEqual(0f, CalendarMath.NightFactor(0.25f, 0.25f), 1e-6f);
        Assert.AreEqual(1f, CalendarMath.NightFactor(0.75f, 0.25f), 1e-6f);
        int plain = 0, biased = 0;
        for (int i = 0; i < 1000; i++)
        {
            float phase = i / 1000f;
            if (CalendarMath.NightFactor(phase) > 0.5f) plain++;
            if (CalendarMath.NightFactor(phase, 0.25f) > 0.5f) biased++;
        }
        Assert.Less(biased, plain - 50, "a biased cycle spends clearly less of it in the dark");
    }
}
