// M20b: which toolbar group a building belongs to. Pure (primitives in, group out) so a test can check every shipped
// building; Assembly-CSharp's BuildingDefinition.ToolbarGroupOf feeds it.
public enum ToolbarGroup
{
    Auto,
    Utilities,
    Services,
    Health,
    Education,
    Parks,
}

public static class ToolbarGroups
{
    public static readonly ToolbarGroup[] All =
    {
        ToolbarGroup.Utilities, ToolbarGroup.Services, ToolbarGroup.Health, ToolbarGroup.Education, ToolbarGroup.Parks,
    };

    public static ToolbarGroup Resolve(ToolbarGroup overrideGroup, bool isUtility, ServiceKind civic, float researchPerDay, float happinessEffect)
    {
        if (overrideGroup != ToolbarGroup.Auto) return overrideGroup;
        switch (civic)
        {
            case ServiceKind.Order:
            case ServiceKind.Fire: return ToolbarGroup.Services;
            case ServiceKind.Health: return ToolbarGroup.Health;
            case ServiceKind.Education: return ToolbarGroup.Education;
        }
        if (researchPerDay > 0f) return ToolbarGroup.Education;
        if (isUtility) return ToolbarGroup.Utilities;
        return happinessEffect > 0f ? ToolbarGroup.Parks : ToolbarGroup.Services;
    }
}
