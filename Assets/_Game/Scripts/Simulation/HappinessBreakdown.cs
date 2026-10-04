// Signed contributions to happiness from the last tick (penalties are negative), for explaining the
// number to the player. Total is clamped to 0..1, matching PopulationSystem.AverageHappiness.
public readonly struct HappinessBreakdown
{
    public readonly float Base;
    public readonly float Unemployment;
    public readonly float Taxes;
    public readonly float Pollution;
    public readonly float Services;
    public readonly float Power;
    public readonly float Homeless;
    public readonly float Technology;       // researched techs' bonus (M11)
    public readonly float Heritage;         // kept historic blocks near homes (M12)
    public readonly float Water;            // homes without water (M13)
    public readonly float Crime;            // crime at homes (M14; already ramped by population)
    public readonly float Fire;             // fire risk at homes (M14; ramped)
    public readonly float Health;           // sickness at homes without health care (M14; ramped)
    public readonly float Ordinances;       // enacted ordinances' happiness (M15), apart from Technology

    public HappinessBreakdown(float baseValue, float unemployment, float taxes, float pollution, float services, float power, float homeless,
        float technology = 0f, float heritage = 0f, float water = 0f, float crime = 0f,
        float fire = 0f, float health = 0f, float ordinances = 0f)
    {
        Ordinances = ordinances;
        Crime = crime;
        Fire = fire;
        Health = health;
        Water = water;
        Heritage = heritage;
        Base = baseValue;
        Unemployment = unemployment;
        Taxes = taxes;
        Pollution = pollution;
        Services = services;
        Power = power;
        Homeless = homeless;
        Technology = technology;
    }

    public float Total => UnityEngine.Mathf.Clamp01(Base + Unemployment + Taxes + Pollution + Services + Power + Homeless + Technology + Heritage + Water + Crime
        + Fire + Health + Ordinances);
}
