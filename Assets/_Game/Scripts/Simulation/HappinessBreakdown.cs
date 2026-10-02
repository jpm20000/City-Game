// Signed contributions to happiness from the last tick (penalties are negative), for explaining the
// number to the player. Total is clamped to 0..1, matching PopulationSystem.AverageHappiness.
public readonly struct HappinessBreakdown
{
    public readonly float Base;
    public readonly float Unemployment;
    public readonly float Taxes;
    public readonly float Pollution;
    public readonly float Services;
    public readonly float Homeless;

    public HappinessBreakdown(float baseValue, float unemployment, float taxes, float pollution, float services, float homeless)
    {
        Base = baseValue;
        Unemployment = unemployment;
        Taxes = taxes;
        Pollution = pollution;
        Services = services;
        Homeless = homeless;
    }

    public float Total => UnityEngine.Mathf.Clamp01(Base + Unemployment + Taxes + Pollution + Services + Homeless);
}
