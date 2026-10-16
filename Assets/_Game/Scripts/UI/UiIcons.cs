// Names of the theme icons the UI asks for (M32c): sprite names under Art/UI/hud, tools and controls, made by
// Tools/gen_ui_art.py from the names files. UiTheme.Icon(name) returns null for a name with no sprite, and the UI then
// shows its text only. UiArtTests checks that every name in All is in the theme. Building icons are not here: they are
// BuildingDefinition.Icon (Art/UI/buildings/<Id>.png).
public static class UiIcons
{
    // HUD
    public const string Money = "money", IncomeUp = "income_up", IncomeDown = "income_down", Population = "population";
    public const string Jobs = "jobs", Power = "power", Water = "water", Research = "research", Age = "age", Date = "date";
    public const string Happy = "happy", Neutral = "neutral", Unhappy = "unhappy";
    public const string Pause = "pause", Play1 = "play_1", Play2 = "play_2", Play3 = "play_3";

    // Toolbar
    public const string Pipe = "pipe", Residential = "residential", Commercial = "commercial", Industrial = "industrial";
    public const string Unzone = "unzone", Demolish = "demolish";
    public const string ViewOff = "view_off";

    // Road tiers 1..5 (RoadTiers.Dirt..Highway).
    private static readonly string[] s_Roads = { "road_dirt", "road_cobble", "road_paved", "road_avenue", "road_highway" };

    public static string Road(byte tier) => tier >= 1 && tier <= s_Roads.Length ? s_Roads[tier - 1] : null;

    public static string Group(ToolbarGroup group)
    {
        switch (group)
        {
            case ToolbarGroup.Utilities: return "group_utilities";
            case ToolbarGroup.Services: return "group_services";
            case ToolbarGroup.Health: return "group_health";
            case ToolbarGroup.Education: return "group_education";
            case ToolbarGroup.Parks: return "group_parks";
            default: return null;
        }
    }

    // Density and Goods have no icon yet (text only).
    public static string View(InfoOverlay.View view)
    {
        switch (view)
        {
            case InfoOverlay.View.Off: return ViewOff;
            case InfoOverlay.View.Power: return "view_power";
            case InfoOverlay.View.Water: return "view_water";
            case InfoOverlay.View.Coverage: return "view_parks";
            case InfoOverlay.View.Pollution: return "view_pollution";
            case InfoOverlay.View.LandValue: return "view_value";
            case InfoOverlay.View.Traffic: return "view_traffic";
            case InfoOverlay.View.Age: return "view_ages";
            case InfoOverlay.View.Order: return "view_crime";
            case InfoOverlay.View.Fire: return "view_fire";
            case InfoOverlay.View.Health: return "view_health";
            case InfoOverlay.View.Education: return "view_schools";
            default: return null;
        }
    }

    // Every name the UI asks for (for the test).
    public static string[] All
    {
        get
        {
            var names = new System.Collections.Generic.List<string>
            {
                Money, IncomeUp, IncomeDown, Population, Jobs, Power, Water, Research, Age, Date, Happy, Neutral, Unhappy,
                Pause, Play1, Play2, Play3, Pipe, Residential, Commercial, Industrial, Unzone, Demolish, ViewOff,
            };
            names.AddRange(s_Roads);
            foreach (ToolbarGroup group in System.Enum.GetValues(typeof(ToolbarGroup)))
                if (Group(group) != null) names.Add(Group(group));
            foreach (InfoOverlay.View view in System.Enum.GetValues(typeof(InfoOverlay.View)))
                if (View(view) != null && !names.Contains(View(view))) names.Add(View(view));
            return names.ToArray();
        }
    }
}
