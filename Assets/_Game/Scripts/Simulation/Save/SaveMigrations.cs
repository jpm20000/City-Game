using System;
using System.Collections.Generic;

// Upgrades older saves one version at a time (v1 -> v2 -> ...), then fits the result to the age
// and tech databases the game runs with. Every format bump adds one step here and a test.
public static class SaveMigrations
{
    // Returns false (with a player-readable reason) for versions it can't handle: older than 1 or
    // newer than SaveData.CurrentVersion. ages/techs may be null (a sim without ages).
    public static bool TryMigrate(SaveData data, AgeDatabase ages, TechDatabase techs, out string error)
    {
        if (data == null) throw new ArgumentNullException(nameof(data));

        if (data.Version < 1 || data.Version > SaveData.CurrentVersion)
        {
            error = $"Save file version {data.Version} is not supported (this game reads 1 to {SaveData.CurrentVersion}).";
            return false;
        }

        if (data.Version == 1)
        {
            V1ToV2(data);
            data.Version = 2;
        }
        if (data.Version == 2)
        {
            V2ToV3(data);
            data.Version = 3;
        }
        if (data.Version == 3)
        {
            V3ToV4(data);
            data.Version = 4;
        }
        if (data.Version == 4)
        {
            V4ToV5(data, techs);
            data.Version = 5;
        }
        if (data.Version == 5)
        {
            V5ToV6(data);
            data.Version = 6;
        }

        data.Buildings ??= new();
        data.Loans ??= new();
        data.Ordinances ??= new();
        data.Broken ??= new();
        data.ActiveEvents ??= new();
        data.RecentEvents ??= new();
        data.RandomState ??= "";
        data.PendingEvent ??= "";
        data.Researched ??= new();
        data.ResearchQueue ??= new();
        data.ActiveResearch ??= "";

        if (ages != null && techs != null)
        {
            if (data.Age == SaveData.NoAge) AdoptLegacyAge(data, ages, techs);
            if (!ages.IsValidIndex(data.Age))
            {
                error = $"Save file is set in age {data.Age}, which this game doesn't have.";
                return false;
            }
            // A cell can't have been built in an age the city hasn't reached.
            if (data.BuiltAges != null)
            {
                for (int i = 0; i < data.BuiltAges.Length; i++)
                {
                    if (data.BuiltAges[i] > data.Age) data.BuiltAges[i] = (byte)data.Age;
                }
            }
        }

        error = null;
        return true;
    }

    // v2 adds ages and research. A v1 city predates ages: it is marked NoAge with empty research
    // and zeroed per-cell arrays; AdoptLegacyAge turns that into an Industrial city when the game
    // has ages.
    private static void V1ToV2(SaveData data)
    {
        int count = Math.Max(0, data.Width * data.Height);
        data.Age = SaveData.NoAge;
        data.Researched = new List<string>();
        data.ActiveResearch = "";
        data.ResearchProgress = 0f;
        data.ResearchQueue = new List<string>();
        data.BuiltAges = new byte[count];
        data.Historic = new byte[count];
    }

    // v3 adds water pipes (M13). Older cities had none: roads carry the water.
    private static void V2ToV3(SaveData data)
    {
        data.Pipes = new byte[Math.Max(0, data.Width * data.Height)];
    }

    // v4 adds funding, loans and ordinances (M15). Older cities fund every line at 100% and owe nothing.
    private static void V3ToV4(SaveData data)
    {
        data.Funding = new float[BudgetSystem.Lines];
        for (int i = 0; i < data.Funding.Length; i++) data.Funding[i] = 1f;
        data.Loans = new List<LoanRecord>();
        data.Ordinances = new List<string>();
    }

    // v5 stores road tiers in the Roads bytes (M16). Older roads become the best street tier the save has
    // researched; Paved for a city saved without ages (it is adopted as Industrial, which has Macadam).
    private static void V4ToV5(SaveData data, TechDatabase techs)
    {
        if (data.Roads == null) return;
        byte tier = RoadTiers.Paved;
        if (techs != null && data.Age != SaveData.NoAge)
        {
            var researched = new HashSet<string>(data.Researched ?? new List<string>());
            tier = RoadTiers.BestStreet(techs, tech => researched.Contains(tech.Id));
        }
        for (int i = 0; i < data.Roads.Length; i++)
        {
            if (data.Roads[i] != 0) data.Roads[i] = tier;
        }
    }

    // v6 adds disasters and events (M17). A city saved before them plays as it always has: the switch is off, no
    // fire, plague, breakdown or event is under way, and the RNG state is empty (DisasterSystem.Restore seeds it).
    private static void V5ToV6(SaveData data)
    {
        int count = Math.Max(0, data.Width * data.Height);
        data.Disasters = false;
        data.RandomState = "";
        data.Fires = new byte[count];
        data.Rubble = new byte[count];
        data.Plague = new byte[count];
        data.PlagueCooldown = 0;
        data.PlagueRemainder = 0f;
        data.Broken = new List<BrokenRecord>();
        data.PendingEvent = "";
        data.PendingDays = 0;
        data.DaysToNextEvent = 0;
        data.ActiveEvents = new List<EventRecord>();
        data.RecentEvents = new List<EventRecord>();
    }

    // A city saved without ages played by today's rules: it becomes an Industrial city with every
    // earlier tech and Industrial's starting techs (Electricity) researched, every grown cell built
    // in Industrial, and its calendar (which started at year 1) moved to the Industrial years.
    private static void AdoptLegacyAge(SaveData data, AgeDatabase ages, TechDatabase techs)
    {
        int legacy = ages.Legacy;
        if (legacy < 0) return;     // no Industrial age to adopt; TryMigrate rejects NoAge

        data.Age = legacy;
        data.Researched = new List<string>();
        foreach (TechDefinition tech in TechSystem.StartingTechs(ages, techs, legacy)) data.Researched.Add(tech.Id);
        data.ActiveResearch = "";
        data.ResearchProgress = 0f;
        data.ResearchQueue = new List<string>();

        int count = data.Width * data.Height;
        data.BuiltAges = new byte[count];
        data.Historic = new byte[count];
        if (data.Levels != null && data.Levels.Length == count)
        {
            for (int i = 0; i < count; i++)
            {
                if (data.Levels[i] > 0) data.BuiltAges[i] = (byte)legacy;
            }
        }

        data.Year += ages[legacy].StartYear - 1;
    }
}
